using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace LuxaforTeamsCalling
{
    public class HidDevice : Device
    {
        public HidDevice(IReadOnlyDictionary<DEVPROPKEY, object> properties)
            : base(properties)
        {
        }

        public string Product => GetString(HidD_GetProductString);
        public string SerialNumber => GetString(HidD_GetSerialNumberString);
        public override string Manufacturer => GetString(HidD_GetManufacturerString);

        public virtual void Write(byte[] buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (buffer.Length == 0)
                throw new ArgumentException(null, nameof(buffer));

            WithHandle(f =>
            {
                f.Write(buffer, 0, buffer.Length);
            }, FileAccess.Write);
        }

        public virtual Task WriteAsync(byte[] buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (buffer.Length == 0)
                throw new ArgumentException(null, nameof(buffer));

            return WithHandleAsync(async f =>
            {
                await f.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            }, FileAccess.Write);
        }

        private string GetString(Func<SafeFileHandle, string, int, bool> func) => WithHandle(f =>
        {
            var buffer = new string('\0', 2000);
            if (!func(f.SafeFileHandle, buffer, buffer.Length * 2))
                return null;

            return buffer.Split('\0').FirstOrDefault();
        });

        public virtual void WithHandle(Action<FileStream> action, FileAccess access = FileAccess.Read)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (Interfaces.Count == 0)
                throw new InvalidOperationException();

            var h = CreateFile(Interfaces[0].Path, GetDesiredAccess(access), FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            using (var file = new FileStream(h, access))
            {
                action(file);
            }
        }

        public virtual async Task WithHandleAsync(Func<FileStream, Task> action, FileAccess access = FileAccess.Read)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (Interfaces.Count == 0)
                throw new InvalidOperationException();

            var h = CreateFile(Interfaces[0].Path, GetDesiredAccess(access), FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            using (var file = new FileStream(h, access))
            {
                await action(file).ConfigureAwait(false);
            }
        }

        public virtual T WithHandle<T>(Func<FileStream, T> action, FileAccess access = FileAccess.Read)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (Interfaces.Count == 0)
                throw new InvalidOperationException();

            var h = CreateFile(Interfaces[0].Path, GetDesiredAccess(access), FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            using (var file = new FileStream(h, access))
            {
                return action(file);
            }
        }

        public virtual async Task<T> WithHandleAsync<T>(Func<FileStream, Task<T>> action, FileAccess access = FileAccess.Read)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (Interfaces.Count == 0)
                throw new InvalidOperationException();

            var h = CreateFile(Interfaces[0].Path, GetDesiredAccess(access), FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            using (var file = new FileStream(h, access))
            {
                return await action(file).ConfigureAwait(false);
            }
        }

        private static int GetDesiredAccess(FileAccess access) => access == FileAccess.Read ? 0 : GENERIC_WRITE;

        public static IEnumerable<LuxaforDevice> Enumerate() => Enumerate(DeviceInterface.GUID_DEVINTERFACE_HID, DIGCF.DIGCF_DEVICEINTERFACE, props => new HidDevice(props))
            .Where(l => l.Interfaces.FirstOrDefault()?.ClassGuid == DeviceInterface.GUID_DEVINTERFACE_HID)
            .Cast<LuxaforDevice>();

        private const int FILE_SHARE_READ = 1;
        private const int FILE_SHARE_WRITE = 2;
        private const short OPEN_EXISTING = 3;
        private const int GENERIC_WRITE = 0x40000000;

        [DllImport("kernel32", SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string lpFileName, int dwDesiredAccess, int dwShareMode, IntPtr lpSecurityAttributes, int dwCreationDisposition, int dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("hid")]
        private static extern bool HidD_GetProductString(SafeFileHandle HidDeviceObject, [MarshalAs(UnmanagedType.LPWStr)] string Buffer, int BufferLength);

        [DllImport("hid")]
        private static extern bool HidD_GetSerialNumberString(SafeFileHandle HidDeviceObject, [MarshalAs(UnmanagedType.LPWStr)] string Buffer, int BufferLength);

        [DllImport("hid")]
        private static extern bool HidD_GetManufacturerString(SafeFileHandle HidDeviceObject, [MarshalAs(UnmanagedType.LPWStr)] string Buffer, int BufferLength);
    }
}
