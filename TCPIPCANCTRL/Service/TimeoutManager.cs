// TCPIPCANCTRL/Service/TimeoutManager.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TcpIpCanCtrl.Event;
using TcpIpCanCtrl.Model;

namespace TcpIpCanCtrl.Service
{
    /// <summary>
    /// 고성능, 저부하 타임아웃 관리 싱글톤.
    /// - PreRegister: 송신 직전 슬롯만 등록(미무장)
    /// - Arm: 실제 송신 완료 시점에 O(1)로 마지막 미무장 항목을 무장
    /// - Complete: 응답 수신 → 헤드 제거(O(1))
    /// - Cancel: 송신 실패/취소 → 마지막 미무장 항목 1개만 제거(O(n) 재적재, 드문 경로)
    /// </summary>

    internal sealed class TimeoutManager : IDisposable
    {
        #region Singleton
        internal static TimeoutManager Instance { get; } = new TimeoutManager();
        #endregion

        #region Types
        private sealed class PendingRequest
        {
            internal readonly JbFrame Command;
            internal DateTime? ArmedAtUtc;    // null = 미무장
            internal TimeSpan Timeout;
            internal volatile bool Removed;   // 큐에서 제거되었는지(스택 고아 참조 방지용)

            internal bool IsArmed => ArmedAtUtc.HasValue;

            internal PendingRequest(JbFrame cmd) => (Command,ArmedAtUtc, Timeout,Removed) = (cmd, null, TimeSpan.Zero,false);

            internal void Arm(DateTime now, TimeSpan timeout) => (ArmedAtUtc, Timeout) = (now, timeout);

            internal bool IsTimedOut(DateTime now)
                => ArmedAtUtc.HasValue && (now - ArmedAtUtc.Value) > Timeout;
        }

        /// <summary>
        /// 장치별 자료구조: 전체 대기열(Q) + 미무장 스택(Unarmed).
        /// </summary>
        private sealed class DeviceQueue
        {
            internal readonly ConcurrentQueue<PendingRequest> Q = new ConcurrentQueue<PendingRequest>();
            internal readonly ConcurrentStack<PendingRequest> Unarmed = new ConcurrentStack<PendingRequest>();
        }

        // Key 규칙: pIndex-pAddr (프로토콜이 주소까지 동일 매칭일 때 권장)
        private static string MakeDeviceKey(JbFrame frame) => $"{frame.pIndex}-{frame.pAddr}";

        private readonly ConcurrentDictionary<string, DeviceQueue> _queues = new ConcurrentDictionary<string, DeviceQueue>();
        private readonly Timer _watchdogTimer; // 단 하나의 감시 타이머
        private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(2);
        private readonly TimeSpan _watchInterval = TimeSpan.FromMilliseconds(100);

        /// <summary>요청 타임아웃 이벤트</summary>
        internal event EventHandler<RequestTimedOutEventArgs> RequestTimedOut;

        private TimeoutManager() => _watchdogTimer = new Timer(CheckForTimeouts, null, _watchInterval, _watchInterval);

        #endregion

        #region Public API
        /// <summary>송신 직전 슬롯만 생성(미무장). Arm에서 실제 카운트다운 시작.</summary>
        internal void PreRegister(JbFrame frame)
        {
            var key = MakeDeviceKey(frame);
            var dq = _queues.GetOrAdd(key, _ => new DeviceQueue());

            var pr = new PendingRequest(frame);
            dq.Q.Enqueue(pr);
            dq.Unarmed.Push(pr);
        }

        /// <summary>
        /// 송신 완료 직후 호출. 마지막 미무장 항목을 O(1)로 꺼내 무장.
        /// (스택에 남아 있는 고아/이미 무장된 항목은 팝하며 폐기)
        /// </summary>
        internal void Arm(JbFrame frame)
        {
            var key = MakeDeviceKey(frame);
            if (!_queues.TryGetValue(key, out var dq)) return;

            while (dq.Unarmed.TryPop(out var pr))
            {
                if (pr.Removed) continue;     // 이미 큐에서 제거된 고아 참조
                if (pr.IsArmed) continue;     // 방어적(이상상황)
                pr.Arm(DateTime.UtcNow, _defaultTimeout);

                return;
            }
        }

        // 매칭 성공 시 true, 없으면 false
        internal bool TryComplete(JbFrame frame)
        {
            var deviceKey = MakeDeviceKey(frame);
            if (!_queues.TryGetValue(deviceKey, out var dq) || !dq.Q.TryDequeue(out var pr)) return false;
            pr.Removed = true; // 스택의 고아 참조 방지
            return true;
        }
        [Obsolete("Use TryComplete instead")]
        /// <summary>응답 수신 시 헤드 제거(O(1)).</summary>
        internal void Complete(JbFrame frame) => Complete(MakeDeviceKey(frame));

        /// <summary>응답 수신 시 헤드 제거(O(1)).</summary>
        private void Complete(string deviceKey)
        {
            if (_queues.TryGetValue(deviceKey, out var dq))
            {
                if (dq.Q.TryDequeue(out var pr))
                {
                    pr.Removed = true; // 스택의 고아 참조 방지
#if DEBUG
                    Console.WriteLine($"[TM] Complete: {deviceKey} (dequeued)");
#endif
                }
            }
        }

        /// <summary>
        /// 송신 실패/취소 시 마지막 미무장 항목 1개만 제거.
        /// - 스택에서 유효한 미무장 항목을 팝(O(1)~)
        /// - 큐에서 해당 참조만 필터링해 재적재(O(n))  ※ 실패 경로이므로 부담 적음
        /// </summary>
        internal void Cancel(JbFrame frame)
        {
            var key = MakeDeviceKey(frame);
            if (!_queues.TryGetValue(key, out var dq)) return;

            // 스택에서 유효한 미무장 하나 찾기
            PendingRequest toRemove;
            while (dq.Unarmed.TryPop(out toRemove))
            {
                if (toRemove.Removed || toRemove.IsArmed) continue;
                toRemove.Removed = true; // 바로 마킹
                break;
            }

            if (toRemove == null) return;

            // 큐 재적재(선택된 1개만 제외)
            DrainFilterReenqueue(dq.Q, p => !ReferenceEquals(p, toRemove));
#if DEBUG
            Console.WriteLine($"[TM] Cancel: {key} (removed-one-unarmed)");
#endif
        }
        #endregion

        #region Watchdog
        private void CheckForTimeouts(object _)
        {
            if (_queues.IsEmpty) return;

            var now = DateTime.UtcNow;

            foreach (var kv in _queues)
            {
                var dq = kv.Value;

                // 헤드부터 확인(FIFO). 헤드가 미무장이면 그 큐는 더 이상 검사하지 않음.
                while (dq.Q.TryPeek(out var head))
                {
                    // 헤드가 미무장 → 이 디바이스 큐에서는 타임아웃 판단 중단
                    // 헤드가 아직 타임아웃 아님 → 중단
                    if (!head.IsArmed || !head.IsTimedOut(now)) break;

                    // 타임아웃! 헤드를 제거하고 이벤트 발생
                    if (dq.Q.TryDequeue(out var timedOut))
                    {
                        timedOut.Removed = true;
                        var args = new RequestTimedOutEventArgs(kv.Key, timedOut.Command);
                        Task.Run(() => RequestTimedOut?.Invoke(this, args)); // 논블로킹 발행
                    }
                    else break;
                }
            }
        }
        #endregion

        #region Helpers
        private static void DrainFilterReenqueue(ConcurrentQueue<PendingRequest> q, Func<PendingRequest, bool> keep)
        {
            var buf = new List<PendingRequest>(16);
            while (q.TryDequeue(out var p)) buf.Add(p);
            foreach (var x in buf)
                if (keep(x)) q.Enqueue(x);
        }
        #endregion

        #region IDisposable
        public void Dispose() => _watchdogTimer?.Dispose();
        #endregion
    }
}