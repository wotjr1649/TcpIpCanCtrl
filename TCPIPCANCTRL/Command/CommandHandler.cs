using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using TcpIpCanCtrl.Controller;
using TcpIpCanCtrl.Interface;
using TcpIpCanCtrl.Service;
using TcpIpCanCtrl.Util;

namespace TcpIpCanCtrl.Command
{
    internal class GenericCommandHandler
    {
        private readonly CommunicationOrchestrator _orchestrator;
        private readonly TCPIPCTRL _controller;
        // JB 박스별 독립 시퀀스 (0~9999 반복)
        private int _seqCounter = 0;

        internal GenericCommandHandler(CommunicationOrchestrator orchestrator, TCPIPCTRL controller)
        {
            _orchestrator = orchestrator;
            _controller = controller;
        }

        internal void Handle(ICommand command)
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
            var builtCommand = CommandProcessor.Build(NextSeq(), payload, encoding); // 시퀀스 번호는 Orchestrator에서 가져옴

            // 6. Enqueue 로직
            _orchestrator.EnqueueCommand(builtCommand);
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
