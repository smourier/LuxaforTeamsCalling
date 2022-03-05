using System.Collections.Generic;
using System.Linq;

namespace LuxaforTeamsCalling
{
    public class LuxaforDevice : HidDevice
    {
        public const ushort HidVendorId = 0x04D8;
        public const ushort HidProductId = 0xF372;

        public LuxaforDevice(IReadOnlyDictionary<DEVPROPKEY, object> properties)
            : base(properties)
        {
        }

        public void SetColor(Color color) => Write(1, 0, color);
        public void FadeTo(Color color, LuxaforLedTarget target, byte duration) => Write(2, (byte)target, color, duration);
        public void Blink(Color color, LuxaforLedTarget target, byte speed, byte repeatCount) => Write(3, (byte)target, color, speed, 0, repeatCount);
        public void SetWave(Color color, LuxaforWave wave, byte speed, byte repeatCount) => Write(4, (byte)wave, color, 0, repeatCount, speed);
        public void SetPattern(LuxaforPattern pattern, byte repeatCount) => Write(6, (byte)pattern, repeatCount);

        public virtual void Write(byte code, byte mode, Color color, byte optional1 = 0, byte optional2 = 0, byte optional3 = 0) => Write(code, mode, color.R, color.G, color.B, optional1, optional2, optional3);
        public virtual void Write(byte code, byte mode, byte r = 0, byte g = 0, byte b = 0, byte optional1 = 0, byte optional2 = 0, byte optional3 = 0)
        {
            var buffer = new byte[] { 0, code, mode, r, g, b, optional1, optional2, optional3 };
            Write(buffer);
        }

        public static new IEnumerable<LuxaforDevice> Enumerate() => Enumerate(DeviceInterface.GUID_DEVINTERFACE_HID, DIGCF.DIGCF_DEVICEINTERFACE, props => new LuxaforDevice(props))
                .Where(l => l.Interfaces.FirstOrDefault()?.HidProductId == HidProductId && l.Interfaces.FirstOrDefault()?.HidVendorId == HidVendorId)
                .Cast<LuxaforDevice>();
    }
}
