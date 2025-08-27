using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TcpIpCanCtrl.Controller;
using TcpIpCanCtrl.Interface;
using TcpIpCanCtrl.Model;
using TcpIpCanCtrl.Service;
using TcpIpCanCtrl.Util;

namespace TcpIpCanCtrl.Command
{
    internal class CommandHandler
    {
        private readonly CommunicationOrchestrator _orchestrator;
        private readonly TcpIpController _controller;
        // JB 박스별 독립 시퀀스 (0~9999 반복)
        private int _seqCounter = -1;

        internal CommandHandler(CommunicationOrchestrator orchestrator, TcpIpController controller)
        {
            _orchestrator = orchestrator;
            _controller = controller;
        }

        internal void Handle(ICommand command, bool requireResponse = true)
        {
            // 1. 공통 유효성 검사
            _controller.ThrowIfDisposed();

            // 2. 커맨드 고유의 유효성 검사
            command.Validate();

            // 3. 페이로드 생성
            string payload = command.GetPayload();

            // 4. 인코딩 선택
            var encoding = command.GetEncodingType() == Encoding_Type.ASCII ? Encodings.Ascii : Encodings.Ksc949;

            // 5. CommandProcessor를 사용해 바이트 조립
            var builtCommand = CommandProcessor.Build(_controller._jbIndex, NextSeq(), payload, encoding); // 시퀀스 번호는 Orchestrator에서 가져옴

            // 6. Enqueue 로직
            _orchestrator.EnqueueCommand(builtCommand.Item1, builtCommand.Item2, requireResponse);
        }

        internal async Task HandleAsync(ICommand command, bool requireResponse = true, CancellationToken ct = default)
        {
            // 1. 공통 유효성 검사
            _controller.ThrowIfDisposed();
            // 2. 커맨드 고유의 유효성 검사
            command.Validate();
            // 3. 페이로드 생성
            string payload = command.GetPayload();
            // 4. 인코딩 선택
            var encoding = command.GetEncodingType() == Encoding_Type.ASCII ? Encodings.Ascii : Encodings.Ksc949;
            // 5. CommandProcessor를 사용해 바이트 조립
            var (owner,frame) = CommandProcessor.Build(_controller._jbIndex, NextSeq(), payload, encoding);
            // 6. Enqueue 로직
            await _orchestrator.EnqueueCommandAsync(owner, frame, requireResponse, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 시퀀스 생성 (0→1→…→9999→0)
        /// JB 박스별 독립적으로 동작
        /// </summary>
        private int NextSeq()
        {
            int originalValue, newValue;

            do
            {
                originalValue = _seqCounter;
                newValue = (originalValue % 10000) + 1;
            } while (Interlocked.CompareExchange(ref _seqCounter, newValue, originalValue) != originalValue);
            return newValue;
        }
    }
}
