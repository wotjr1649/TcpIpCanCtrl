using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace TcpIpCanCtrl.Util
{
    /// <summary>
    /// IMemoryOwner의 범위를 명확히 하는 헬퍼 클래스.
    /// MemoryPool에서 빌려온 Memory의 실제 사용 길이를 제한합니다.
    /// 이 클래스는 IMemoryOwner를 래핑하며, Dispose() 호출 시 내부 IMemoryOwner도 해제합니다.
    /// 따라서 반드시 using 문과 함께 사용하여 자원이 누수되지 않도록 관리해야 합니다.
    /// </summary>
    internal sealed class MemoryOwner : IMemoryOwner<byte>
    {
        // 1. 유일한 인스턴스를 저장할 비공개 정적 필드

        private IMemoryOwner<byte> _owner;
        private readonly int _length;

        public MemoryOwner(IMemoryOwner<byte> owner, int length)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _length = length;
        }

        public Memory<byte> Memory => _owner.Memory.Slice(0, _length);

        public void Dispose()
        {
            _owner?.Dispose();
            _owner = null;
        }
    }
}
