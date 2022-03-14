using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;

namespace LuxaforTeamsCalling
{
    public class Device
    {
        private readonly List<DeviceInterface> _interfaces = new List<DeviceInterface>();

        public Device(IReadOnlyDictionary<DEVPROPKEY, object> properties)
        {
            if (properties == null)
                throw new ArgumentNullException(nameof(properties));

            Properties = properties;
        }

        public IReadOnlyDictionary<DEVPROPKEY, object> Properties { get; }
        public IReadOnlyList<DeviceInterface> Interfaces => _interfaces;

        public T GetPropertyValue<T>(DEVPROPKEY pk, T defaultValue = default)
        {
            if (Properties.TryGetValue(pk, out var value))
                return (T)value;

            return defaultValue;
        }

        public virtual string Name => GetPropertyValue<string>(DEVPKEY_NAME);
        public virtual string FriendlyName => GetPropertyValue<string>(DEVPKEY_Device_FriendlyName);
        public virtual string Description => GetPropertyValue<string>(DEVPKEY_Device_DeviceDesc);
        public virtual string Manufacturer => GetPropertyValue<string>(DEVPKEY_Device_Manufacturer);
        public virtual string Class => GetPropertyValue<string>(DEVPKEY_Device_Class);
        public virtual Guid ClassGuid => GetPropertyValue<Guid>(DEVPKEY_Device_ClassGuid);
        public virtual bool IsPresent => GetPropertyValue<bool>(DEVPKEY_Device_IsPresent);

        public override string ToString() => Name;

        public static IEnumerable<Device> Enumerate(Guid? classGuid = null, DIGCF flags = DIGCF.DIGCF_ALLCLASSES | DIGCF.DIGCF_DEVICEINTERFACE, Func<IReadOnlyDictionary<DEVPROPKEY, object>, Device> creator = null)
        {
            IntPtr handle;
            if (classGuid == null)
            {
                handle = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, flags);
            }
            else
            {
                handle = SetupDiGetClassDevsW(classGuid.Value, null, IntPtr.Zero, flags & ~DIGCF.DIGCF_ALLCLASSES);
            }

            if (handle == INVALID_HANDLE_VALUE)
            {
                Debug.WriteLine("SetupDiGetClassDevsW failed:" + Marshal.GetLastWin32Error());
                yield break;
            }

            try
            {
                var index = 0;
                var data = new SP_DEVINFO_DATA
                {
                    cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>()
                };

                while (SetupDiEnumDeviceInfo(handle, index, ref data))
                {
                    SetupDiGetDevicePropertyKeys(handle, ref data, IntPtr.Zero, 0, out var count, 0);
                    if (count == 0)
                    {
                        Debug.WriteLine("SetupDiGetDevicePropertyKeys1 failed:" + Marshal.GetLastWin32Error());
                        continue;
                    }

                    var pks = new DEVPROPKEY[count];
                    if (!SetupDiGetDevicePropertyKeys(handle, ref data, pks, pks.Length, out _, 0))
                    {
                        Debug.WriteLine("SetupDiGetDevicePropertyKeys2 failed:" + Marshal.GetLastWin32Error());
                        continue;
                    }

                    var props = new Dictionary<DEVPROPKEY, object>();
                    foreach (var pk in pks)
                    {
                        if (TryGetDeviceProperty(handle, ref data, pk, out var value))
                        {
                            props.Add(pk, value);
                        }
                    }

                    Device device;
                    if (creator != null)
                    {
                        device = creator(props);
                        if (device == null)
                            continue;
                    }
                    else
                    {
                        device = new Device(props);
                    }

                    // note: https://docs.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetclassdevsw#device-interface-class-control-options
                    if (flags.HasFlag(DIGCF.DIGCF_DEVICEINTERFACE))
                    {
                        IEnumerable<Guid> ifaceGuids;
                        if (classGuid.HasValue)
                        {
                            // in this case we'll have only 1 interface with informations such as vendor, etc.
                            ifaceGuids = new[] { classGuid.Value };
                        }
                        else
                        {
                            // in this case we'll have all interfaces with less informations
                            ifaceGuids = DeviceInterface.RegistryClassGuids;
                        }

                        foreach (var ifaceGuid in ifaceGuids)
                        {
                            var idata = new SP_DEVICE_INTERFACE_DATA
                            {
                                cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>()
                            };

                            var iindex = 0;
                            while (SetupDiEnumDeviceInterfaces(handle, ref data, ifaceGuid, iindex, ref idata))
                            {
                                SetupDiGetDeviceInterfacePropertyKeys(handle, ref idata, IntPtr.Zero, 0, out count, 0);
                                if (count == 0)
                                {
                                    Debug.WriteLine("SetupDiGetDeviceInterfacePropertyKey1 failed:" + Marshal.GetLastWin32Error());
                                    continue;
                                }

                                pks = new DEVPROPKEY[count];
                                if (!SetupDiGetDeviceInterfacePropertyKeys(handle, ref idata, pks, pks.Length, out _, 0))
                                {
                                    Debug.WriteLine("SetupDiGetDeviceInterfacePropertyKey2 failed:" + Marshal.GetLastWin32Error());
                                    continue;
                                }

                                var iprops = new Dictionary<DEVPROPKEY, object>();
                                foreach (var pk in pks)
                                {
                                    if (TryGetDeviceInterfaceProperty(handle, ref idata, pk, out var value))
                                    {
                                        iprops.Add(pk, value);
                                    }
                                }

                                var path = GetDeviceInterfacePath(handle, ref idata);
                                if (path != null)
                                {
                                    var iface = new DeviceInterface(iprops, path);
                                    device._interfaces.Add(iface);
                                }
                                iindex++;
                            }
                        }
                    }

                    yield return device;
                    index++;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(handle);
            }
        }

        private static bool TryGetDeviceProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA data, DEVPROPKEY pk, out object value)
        {
            value = null;
            if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out var type, IntPtr.Zero, 0, out var size, 0))
            {
                var gle = Marshal.GetLastWin32Error();
                if (gle == ERROR_NOT_FOUND)
                    return false;

                int i;
                long l;
                string s;
                switch (type)
                {
                    case DEVPROPTYPE.DEVPROP_TYPE_BOOLEAN:
                        i = 0;
                        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out _, ref i, Marshal.SizeOf<int>(), out _, 0))
                            return false;

                        value = i != 0;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_SBYTE:
                        i = 0;
                        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out _, ref i, Marshal.SizeOf<sbyte>(), out _, 0))
                            return false;

                        value = (sbyte)i;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_BYTE:
                        i = 0;
                        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out _, ref i, Marshal.SizeOf<byte>(), out _, 0))
                            return false;

                        value = (byte)i;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_UINT16:
                        i = 0;
                        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out _, ref i, Marshal.SizeOf<ushort>(), out _, 0))
                            return false;

                        value = (ushort)i;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_INT16:
                        i = 0;
                        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out _, ref i, Marshal.SizeOf<short>(), out _, 0))
                            return false;

                        value = (short)i;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_UINT32:
                        i = 0;
                        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out _, ref i, Marshal.SizeOf<uint>(), out _, 0))
                            return false;

                        value = (uint)i;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_DEVPROPTYPE:
                    case DEVPROPTYPE.DEVPROP_TYPE_INT32:
                    case DEVPROPTYPE.DEVPROP_TYPE_ERROR:
                    case DEVPROPTYPE.DEVPROP_TYPE_NTSTATUS:
                        i = 0;
                        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out _, ref i, Marshal.SizeOf<int>(), out _, 0))
                            return false;

                        if (type == DEVPROPTYPE.DEVPROP_TYPE_DEVPROPTYPE)
                        {
                            value = (DEVPROPTYPE)i;
                        }
                        else
                        {
                            value = i;
                        }
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_UINT64:
                        l = 0L;
                        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out _, ref l, Marshal.SizeOf<ulong>(), out _, 0))
                            return false;

                        value = (ulong)l;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_CURRENCY:
                    case DEVPROPTYPE.DEVPROP_TYPE_INT64:
                        l = 0L;
                        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out _, ref l, Marshal.SizeOf<long>(), out _, 0))
                            return false;

                        value = l;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_SECURITY_DESCRIPTOR_STRING:
                    case DEVPROPTYPE.DEVPROP_TYPE_STRING_INDIRECT:
                    case DEVPROPTYPE.DEVPROP_TYPE_STRING:
                        s = new string('\0', (size - 1) / 2);
                        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out _, s, size, out _, 0))
                            return false;

                        value = s;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_STRING_LIST:
                        s = new string('\0', (size - 1) / 2);
                        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out _, s, size, out _, 0))
                            return false;

                        value = s.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_BINARY:
                    case DEVPROPTYPE.DEVPROP_TYPE_SECURITY_DESCRIPTOR:
                        var bytes = new byte[size];
                        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out _, bytes, size, out _, 0))
                            return false;

                        if (type == DEVPROPTYPE.DEVPROP_TYPE_SECURITY_DESCRIPTOR)
                        {
                            value = new RawSecurityDescriptor(bytes, 0);
                        }
                        else
                        {
                            value = bytes;
                        }
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_GUID:
                    case DEVPROPTYPE.DEVPROP_TYPE_DECIMAL:
                        var guid = Guid.Empty;
                        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out _, ref guid, Marshal.SizeOf<Guid>(), out _, 0))
                            return false;

                        value = guid;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_FILETIME:
                        l = 0L;
                        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out _, ref l, Marshal.SizeOf<long>(), out _, 0))
                            return false;

                        value = DateTime.FromFileTime(l);
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_NULL:
                        value = null;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_FLOAT:
                        var fl = 0f;
                        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out _, ref fl, Marshal.SizeOf<float>(), out _, 0))
                            return false;

                        value = fl;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_DOUBLE:
                    case DEVPROPTYPE.DEVPROP_TYPE_DATE:
                        var dbl = 0d;
                        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out _, ref dbl, Marshal.SizeOf<double>(), out _, 0))
                            return false;

                        value = dbl;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_DEVPROPKEY:
                        var dpk = new DEVPROPKEY();
                        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref pk, out _, ref dpk, Marshal.SizeOf<Guid>(), out _, 0))
                            return false;

                        value = dpk;
                        return true;
                }

            }
            return false;
        }

        private static bool TryGetDeviceInterfaceProperty(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA data, DEVPROPKEY pk, out object value)
        {
            value = null;
            if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out var type, IntPtr.Zero, 0, out var size, 0))
            {
                var gle = Marshal.GetLastWin32Error();
                if (gle == ERROR_NOT_FOUND)
                    return false;

                int i;
                long l;
                string s;
                switch (type)
                {
                    case DEVPROPTYPE.DEVPROP_TYPE_BOOLEAN:
                        i = 0;
                        if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out _, ref i, Marshal.SizeOf<int>(), out _, 0))
                            return false;

                        value = i != 0;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_SBYTE:
                        i = 0;
                        if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out _, ref i, Marshal.SizeOf<sbyte>(), out _, 0))
                            return false;

                        value = (sbyte)i;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_BYTE:
                        i = 0;
                        if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out _, ref i, Marshal.SizeOf<byte>(), out _, 0))
                            return false;

                        value = (byte)i;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_UINT16:
                        i = 0;
                        if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out _, ref i, Marshal.SizeOf<ushort>(), out _, 0))
                            return false;

                        value = (ushort)i;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_INT16:
                        i = 0;
                        if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out _, ref i, Marshal.SizeOf<short>(), out _, 0))
                            return false;

                        value = (short)i;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_UINT32:
                        i = 0;
                        if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out _, ref i, Marshal.SizeOf<uint>(), out _, 0))
                            return false;

                        value = (uint)i;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_DEVPROPTYPE:
                    case DEVPROPTYPE.DEVPROP_TYPE_INT32:
                    case DEVPROPTYPE.DEVPROP_TYPE_ERROR:
                    case DEVPROPTYPE.DEVPROP_TYPE_NTSTATUS:
                        i = 0;
                        if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out _, ref i, Marshal.SizeOf<int>(), out _, 0))
                            return false;

                        if (type == DEVPROPTYPE.DEVPROP_TYPE_DEVPROPTYPE)
                        {
                            value = (DEVPROPTYPE)i;
                        }
                        else
                        {
                            value = i;
                        }
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_UINT64:
                        l = 0L;
                        if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out _, ref l, Marshal.SizeOf<ulong>(), out _, 0))
                            return false;

                        value = (ulong)l;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_CURRENCY:
                    case DEVPROPTYPE.DEVPROP_TYPE_INT64:
                        l = 0L;
                        if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out _, ref l, Marshal.SizeOf<long>(), out _, 0))
                            return false;

                        value = l;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_SECURITY_DESCRIPTOR_STRING:
                    case DEVPROPTYPE.DEVPROP_TYPE_STRING_INDIRECT:
                    case DEVPROPTYPE.DEVPROP_TYPE_STRING:
                        s = new string('\0', (size - 1) / 2);
                        if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out _, s, size, out _, 0))
                            return false;

                        value = s;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_STRING_LIST:
                        s = new string('\0', (size - 1) / 2);
                        if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out _, s, size, out _, 0))
                            return false;

                        value = s.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_BINARY:
                    case DEVPROPTYPE.DEVPROP_TYPE_SECURITY_DESCRIPTOR:
                        var bytes = new byte[size];
                        if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out _, bytes, size, out _, 0))
                            return false;

                        if (type == DEVPROPTYPE.DEVPROP_TYPE_SECURITY_DESCRIPTOR)
                        {
                            value = new RawSecurityDescriptor(bytes, 0);
                        }
                        else
                        {
                            value = bytes;
                        }
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_GUID:
                    case DEVPROPTYPE.DEVPROP_TYPE_DECIMAL:
                        var guid = Guid.Empty;
                        if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out _, ref guid, Marshal.SizeOf<Guid>(), out _, 0))
                            return false;

                        value = guid;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_FILETIME:
                        l = 0L;
                        if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out _, ref l, Marshal.SizeOf<long>(), out _, 0))
                            return false;

                        value = DateTime.FromFileTime(l);
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_NULL:
                        value = null;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_FLOAT:
                        var fl = 0f;
                        if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out _, ref fl, Marshal.SizeOf<float>(), out _, 0))
                            return false;

                        value = fl;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_DOUBLE:
                    case DEVPROPTYPE.DEVPROP_TYPE_DATE:
                        var dbl = 0d;
                        if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out _, ref dbl, Marshal.SizeOf<double>(), out _, 0))
                            return false;

                        value = dbl;
                        return true;

                    case DEVPROPTYPE.DEVPROP_TYPE_DEVPROPKEY:
                        var dpk = new DEVPROPKEY();
                        if (!SetupDiGetDeviceInterfacePropertyW(deviceInfoSet, ref data, ref pk, out _, ref dpk, Marshal.SizeOf<Guid>(), out _, 0))
                            return false;

                        value = dpk;
                        return true;
                }

            }
            return false;
        }

        private static readonly Lazy<IDictionary<DEVPROPKEY, string>> _propKeyNames = new Lazy<IDictionary<DEVPROPKEY, string>>(GetDEVPROPKEYNames, true);

        public static string GetDEVPROPKEYName(DEVPROPKEY pk)
        {
            if (!_propKeyNames.Value.TryGetValue(pk, out var name) || name == null)
            {
                // double check
                name = GetNameFromPropertyKey(pk) ?? string.Empty;
                _propKeyNames.Value[pk] = name;
            }

            return name;
        }

        private static IDictionary<DEVPROPKEY, string> GetDEVPROPKEYNames()
        {
            // well known undefined ones
            var dic = new Dictionary<DEVPROPKEY, string>
            {
                [DeviceInterface.DEVPKEY_DeviceInterface_ClassGuid] = "Device Interface Class Guid",
                [DeviceInterface.DEVPKEY_DeviceInterface_Enabled] = "Device Interface Enabled",
                [DEVPKEY_PciDevice_SupportedLinkSubState] = "Pci Device Supported Link Sub State",
                [PKEY_PNPX_LastNotificationTime] = "PNPX Last Notification Time",
                [PKEY_WSD_AppSeqInstanceID] = "WSD App Seq Instance ID",
                [PROCESSOR_NUMBER_PKEY] = "Processor Number",
                [PKEY_SSDP_DevLifeTime] = "SSDP Dev Life Time",
                [PKEY_SSDP_NetworkInterface] = "SSDP Network Interface",
                [DEVPKEY_PciDevice_OnPostPath] = "Pci Device On Post Path",
                [DEVPKEY_DeviceInterface_ReferenceString] = "Device Interface Reference String"
            };

            if (IntPtr.Size == 8) // we don't support this for x86
            {
                using (var file = new FileStream(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "devmgr.dll"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var reader = new BinaryReader(file);
                    const int IMAGE_DOS_HEADERSize = 64;
                    reader.BaseStream.Seek(IMAGE_DOS_HEADERSize - 4, SeekOrigin.Begin);
                    var elfanew = reader.ReadInt32();
                    reader.BaseStream.Seek(elfanew, SeekOrigin.Begin);
                    if (reader.ReadInt32() == 0x4550) // PE
                    {
                        var fileHeader = ReadStructure<IMAGE_FILE_HEADER>(reader);
                        IntPtr imageBase;

                        const ushort IMAGE_FILE_32BIT_MACHINE = 0x0100;
                        var is32 = (fileHeader.Characteristics & IMAGE_FILE_32BIT_MACHINE) != 0;
                        if (is32)
                        {
                            var optionalHeader32 = ReadStructure<IMAGE_OPTIONAL_HEADER32>(reader);
                            imageBase = new IntPtr((int)optionalHeader32.ImageBase);
                        }
                        else
                        {
                            var optionalHeader64 = ReadStructure<IMAGE_OPTIONAL_HEADER64>(reader);
                            imageBase = new IntPtr((long)optionalHeader64.ImageBase);
                        }

                        var sections = new IMAGE_SECTION_HEADER[fileHeader.NumberOfSections];
                        for (var i = 0; i < sections.Length; i++)
                        {
                            sections[i] = ReadStructure<IMAGE_SECTION_HEADER>(reader);
                        }

                        uint? getOffset(uint va)
                        {
                            foreach (var section in sections)
                            {
                                if (va >= section.VirtualAddress && va < (section.VirtualAddress + section.VirtualSize))
                                    return section.PointerToRawData - section.VirtualAddress + va;
                            }
                            return null;
                        }

                        var search = new byte[] { 0x4C, 0x8D, 0x3D }; // lea r15, [32-bit offset]

                        var bytes = new byte[search.Length];
                        do
                        {
                            var pos = file.Position;
                            if (file.Read(bytes, 0, bytes.Length) != bytes.Length)
                                break;

                            if (!bytes.SequenceEqual(search))
                            {
                                file.Position = pos + 1;
                                continue;
                            }

                            var offset = reader.ReadInt32();
                            file.Seek(offset - 0x200, SeekOrigin.Current); // note sure why 0x200 here

                            if (file.Position >= file.Length)
                            {
                                file.Position = pos + 1;
                                continue;
                            }

                            var startPropKeysList = file.Position;

                            var startRva = reader.ReadInt64();
                            var startVa = startRva - imageBase.ToInt64();

                            var startOffset = getOffset((uint)startVa);
                            if (!startOffset.HasValue)
                            {
                                file.Position = pos + 1;
                                continue;
                            }

                            file.Seek(startOffset.Value, SeekOrigin.Begin);
                            var startPk = ReadStructure<DEVPROPKEY>(reader);
                            if (!startPk.Equals(DEVPKEY_NAME))
                            {
                                file.Position = pos + 1;
                                continue;
                            }

                            var h = LoadLibraryEx("devmgr.dll", IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
                            if (h != IntPtr.Zero)
                            {
                                try
                                {
                                    // found! ok, now read all
                                    var sb = new StringBuilder(1024);
                                    do
                                    {
                                        file.Position = startPropKeysList;
                                        var pkRva = reader.ReadInt64();
                                        if (pkRva < imageBase.ToInt64())
                                            break;

                                        var pkIndex = reader.ReadUInt32();

                                        var pkVa = pkRva - imageBase.ToInt64();
                                        var pkOffset = getOffset((uint)pkVa);
                                        if (!pkOffset.HasValue)
                                            break;

                                        file.Seek(pkOffset.Value, SeekOrigin.Begin);
                                        var pk = ReadStructure<DEVPROPKEY>(reader);

                                        LoadString(h, pkIndex, sb, sb.Capacity);
                                        var s = sb.ToString();
                                        if (string.IsNullOrWhiteSpace(s))
                                        {
                                            dic[pk] = null;
                                        }
                                        else
                                        {
                                            dic[pk] = s;
                                        }

                                        startPropKeysList += 16;
                                    }
                                    while (true);
                                }
                                finally
                                {
                                    FreeLibrary(h);
                                }
                            }
                        }
                        while (true);
                    }
                }
            }
            return dic;
        }

        private static string GetDeviceInterfacePath(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData)
        {
            SetupDiGetDeviceInterfaceDetailW(deviceInfoSet, ref deviceInterfaceData, IntPtr.Zero, 0, out var size, IntPtr.Zero);
            if (size == 0)
            {
                Debug.WriteLine("SetupDiGetDeviceInterfaceDetailW1 failed:" + Marshal.GetLastWin32Error());
                return null;
            }

            var ptr = Marshal.AllocCoTaskMem(size);
            try
            {
                Marshal.WriteInt32(ptr, IntPtr.Size == 8 ? 8 : 6); // sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA W or A) = 8
                if (!SetupDiGetDeviceInterfaceDetailW(deviceInfoSet, ref deviceInterfaceData, ptr, size, out _, IntPtr.Zero))
                {
                    Debug.WriteLine("SetupDiGetDeviceInterfaceDetailW2 failed:" + Marshal.GetLastWin32Error());
                    return null;
                }

                return Marshal.PtrToStringUni(ptr + 4);
            }
            finally
            {
                Marshal.FreeCoTaskMem(ptr);
            }
        }

        private static T ReadStructure<T>(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(Marshal.SizeOf<T>());
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        private static string GetNameFromPropertyKey(DEVPROPKEY pk)
        {
            PSGetNameFromPropertyKey(ref pk, out var ptr);
            if (ptr == IntPtr.Zero)
                return null;

            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                Marshal.FreeCoTaskMem(ptr);
            }
        }

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr SetupDiGetClassDevsW([MarshalAs(UnmanagedType.LPStruct)] Guid ClassGuid, string Enumerator, IntPtr hwndParent, DIGCF Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr SetupDiGetClassDevsW(IntPtr ClassGuid, string Enumerator, IntPtr hwndParent, DIGCF Flags);

        [DllImport("setupapi", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, int MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDevicePropertyW(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, IntPtr PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDevicePropertyW(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, [In, Out] byte[] PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDevicePropertyW(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, ref int PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDevicePropertyW(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, ref Guid PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDevicePropertyW(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, ref long PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDevicePropertyW(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, ref float PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDevicePropertyW(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, ref double PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDevicePropertyW(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, ref DEVPROPKEY PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDevicePropertyW(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, string PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDevicePropertyKeys(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, IntPtr PropertyKeyArray, int PropertyKeyCount, out int RequiredPropertyKeyCount, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDevicePropertyKeys(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, [In, Out] DEVPROPKEY[] PropertyKeyArray, int PropertyKeyCount, out int RequiredPropertyKeyCount, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfacePropertyW(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, IntPtr PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfacePropertyW(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, [In, Out] byte[] PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfacePropertyW(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, ref int PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfacePropertyW(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, ref Guid PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfacePropertyW(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, ref long PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfacePropertyW(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, ref float PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfacePropertyW(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, ref double PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfacePropertyW(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, ref DEVPROPKEY PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfacePropertyW(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, ref DEVPROPKEY PropertyKey, out DEVPROPTYPE PropertyType, string PropertyBuffer, int PropertyBufferSize, out int RequiredSize, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfacePropertyKeys(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr PropertyKeyArray, int PropertyKeyCount, out int RequiredPropertyKeyCount, int Flags);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfacePropertyKeys(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, [In, Out] DEVPROPKEY[] PropertyKeyArray, int PropertyKeyCount, out int RequiredPropertyKeyCount, int Flags);

        [DllImport("setupapi", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, [MarshalAs(UnmanagedType.LPStruct)] Guid InterfaceClassGuid, int MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, int DeviceInterfaceDetailDataSize, out int RequiredSize, IntPtr DeviceInfoData);

        [DllImport("propsys")]
        private static extern int PSGetNameFromPropertyKey(ref DEVPROPKEY propkey, out IntPtr ppszCanonicalName);

        [DllImport("user32", CharSet = CharSet.Auto)]
        private static extern int LoadString(IntPtr hInstance, uint uID, StringBuilder lpBuffer, int cchBufferMax);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, int dwFlags);

        [DllImport("kernel32")]
        private static extern bool FreeLibrary(IntPtr hModule);

        [StructLayout(LayoutKind.Explicit)]
        private struct IMAGE_SECTION_HEADER
        {
            [FieldOffset(0)]
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public char[] NameCharArray;

            [FieldOffset(8)]
            public uint VirtualSize;

            [FieldOffset(12)]
            public uint VirtualAddress;

            [FieldOffset(16)]
            public uint SizeOfRawData;

            [FieldOffset(20)]
            public uint PointerToRawData;

            [FieldOffset(24)]
            public uint PointerToRelocations;

            [FieldOffset(28)]
            public uint PointerToLinenumbers;

            [FieldOffset(32)]
            public ushort NumberOfRelocations;

            [FieldOffset(34)]
            public ushort NumberOfLinenumbers;

            [FieldOffset(36)]
            public uint Characteristics;

            public override string ToString() => Name;
            public string Name
            {
                get
                {
                    var end = Array.IndexOf(NameCharArray, '\0');
                    if (end < 0)
                        return new string(NameCharArray);

                    return new string(NameCharArray, 0, end);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_DATA_DIRECTORY
        {
            public uint VirtualAddress;
            public uint Size;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_OPTIONAL_HEADER32
        {
            public ushort Magic;
            public byte MajorLinkerVersion;
            public byte MinorLinkerVersion;
            public uint SizeOfCode;
            public uint SizeOfInitializedData;
            public uint SizeOfUninitializedData;
            public uint AddressOfEntryPoint;
            public uint BaseOfCode;
            public uint BaseOfData;
            public uint ImageBase;
            public uint SectionAlignment;
            public uint FileAlignment;
            public ushort MajorOperatingSystemVersion;
            public ushort MinorOperatingSystemVersion;
            public ushort MajorImageVersion;
            public ushort MinorImageVersion;
            public ushort MajorSubsystemVersion;
            public ushort MinorSubsystemVersion;
            public uint Win32VersionValue;
            public uint SizeOfImage;
            public uint SizeOfHeaders;
            public uint CheckSum;
            public ushort Subsystem;
            public ushort DllCharacteristics;
            public uint SizeOfStackReserve;
            public uint SizeOfStackCommit;
            public uint SizeOfHeapReserve;
            public uint SizeOfHeapCommit;
            public uint LoaderFlags;
            public uint NumberOfRvaAndSizes;

            public IMAGE_DATA_DIRECTORY ExportTable;
            public IMAGE_DATA_DIRECTORY ImportTable;
            public IMAGE_DATA_DIRECTORY ResourceTable;
            public IMAGE_DATA_DIRECTORY ExceptionTable;
            public IMAGE_DATA_DIRECTORY CertificateTable;
            public IMAGE_DATA_DIRECTORY BaseRelocationTable;
            public IMAGE_DATA_DIRECTORY Debug;
            public IMAGE_DATA_DIRECTORY Architecture;
            public IMAGE_DATA_DIRECTORY GlobalPtr;
            public IMAGE_DATA_DIRECTORY TLSTable;
            public IMAGE_DATA_DIRECTORY LoadConfigTable;
            public IMAGE_DATA_DIRECTORY BoundImport;
            public IMAGE_DATA_DIRECTORY IAT;
            public IMAGE_DATA_DIRECTORY DelayImportDescriptor;
            public IMAGE_DATA_DIRECTORY CLRRuntimeHeader;
            public IMAGE_DATA_DIRECTORY Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_OPTIONAL_HEADER64
        {
            public ushort Magic;
            public byte MajorLinkerVersion;
            public byte MinorLinkerVersion;
            public uint SizeOfCode;
            public uint SizeOfInitializedData;
            public uint SizeOfUninitializedData;
            public uint AddressOfEntryPoint;
            public uint BaseOfCode;
            public ulong ImageBase;
            public uint SectionAlignment;
            public uint FileAlignment;
            public ushort MajorOperatingSystemVersion;
            public ushort MinorOperatingSystemVersion;
            public ushort MajorImageVersion;
            public ushort MinorImageVersion;
            public ushort MajorSubsystemVersion;
            public ushort MinorSubsystemVersion;
            public uint Win32VersionValue;
            public uint SizeOfImage;
            public uint SizeOfHeaders;
            public uint CheckSum;
            public ushort Subsystem;
            public ushort DllCharacteristics;
            public ulong SizeOfStackReserve;
            public ulong SizeOfStackCommit;
            public ulong SizeOfHeapReserve;
            public ulong SizeOfHeapCommit;
            public uint LoaderFlags;
            public uint NumberOfRvaAndSizes;
            public IMAGE_DATA_DIRECTORY ExportTable;
            public IMAGE_DATA_DIRECTORY ImportTable;
            public IMAGE_DATA_DIRECTORY ResourceTable;
            public IMAGE_DATA_DIRECTORY ExceptionTable;
            public IMAGE_DATA_DIRECTORY CertificateTable;
            public IMAGE_DATA_DIRECTORY BaseRelocationTable;
            public IMAGE_DATA_DIRECTORY Debug;
            public IMAGE_DATA_DIRECTORY Architecture;
            public IMAGE_DATA_DIRECTORY GlobalPtr;
            public IMAGE_DATA_DIRECTORY TLSTable;
            public IMAGE_DATA_DIRECTORY LoadConfigTable;
            public IMAGE_DATA_DIRECTORY BoundImport;
            public IMAGE_DATA_DIRECTORY IAT;
            public IMAGE_DATA_DIRECTORY DelayImportDescriptor;
            public IMAGE_DATA_DIRECTORY CLRRuntimeHeader;
            public IMAGE_DATA_DIRECTORY Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_FILE_HEADER
        {
            public ushort Machine;
            public ushort NumberOfSections;
            public uint TimeDateStamp;
            public uint PointerToSymbolTable;
            public uint NumberOfSymbols;
            public ushort SizeOfOptionalHeader;
            public ushort Characteristics;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public int DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DETAIL_DATA_W
        {
            public int cbSize;
            public byte DevicePath;
        }

        private const int LOAD_LIBRARY_AS_DATAFILE = 2;
        internal static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const int ERROR_NOT_FOUND = 1168;
        private const int DEVPROP_TYPEMOD_ARRAY = 0x00001000;
        private const int DEVPROP_TYPEMOD_LIST = 0x00002000;

        private static readonly Guid FMTID_Storage = new Guid("b725f130-47ef-101a-a5f1-02608c9eebac");
        private static readonly DEVPROPKEY DEVPKEY_NAME = new DEVPROPKEY(FMTID_Storage, 10);

        private static readonly Guid FMTID_Device = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
        private static readonly DEVPROPKEY DEVPKEY_Device_DeviceDesc = new DEVPROPKEY(FMTID_Device, 2);
        private static readonly DEVPROPKEY DEVPKEY_Device_FriendlyName = new DEVPROPKEY(FMTID_Device, 14);
        private static readonly DEVPROPKEY DEVPKEY_Device_ClassGuid = new DEVPROPKEY(FMTID_Device, 10);
        private static readonly DEVPROPKEY DEVPKEY_Device_Class = new DEVPROPKEY(FMTID_Device, 9);
        private static readonly DEVPROPKEY DEVPKEY_Device_Manufacturer = new DEVPROPKEY(FMTID_Device, 13);
        private static readonly DEVPROPKEY DEVPKEY_Device_IsPresent = new DEVPROPKEY(new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), 5);
        internal static readonly DEVPROPKEY DEVPKEY_Device_InstanceId = new DEVPROPKEY(new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), 256);

        // pk that are unknown but present in SDK .h
        private static readonly DEVPROPKEY DEVPKEY_PciDevice_SupportedLinkSubState = new DEVPROPKEY(new Guid("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62"), 36);
        private static readonly DEVPROPKEY PKEY_PNPX_LastNotificationTime = new DEVPROPKEY(new Guid("656a3bb3-ecc0-43fd-8477-4ae0404a96cd"), 4);
        private static readonly DEVPROPKEY PKEY_WSD_AppSeqInstanceID = new DEVPROPKEY(new Guid("92506491-ff95-4724-a05a-5b81885a7c92"), 4100);
        private static readonly DEVPROPKEY PROCESSOR_NUMBER_PKEY = new DEVPROPKEY(new Guid("5724c81d-d5af-4c1f-a103-a06e28f204c6"), 1);
        private static readonly DEVPROPKEY PKEY_SSDP_DevLifeTime = new DEVPROPKEY(new Guid("656a3bb3-ecc0-43fd-8477-4ae0404a96cd"), 24577);
        private static readonly DEVPROPKEY PKEY_SSDP_NetworkInterface = new DEVPROPKEY(new Guid("656a3bb3-ecc0-43fd-8477-4ae0404a96cd"), 24578);
        private static readonly DEVPROPKEY DEVPKEY_PciDevice_OnPostPath = new DEVPROPKEY(new Guid("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62"), 37);
        private static readonly DEVPROPKEY DEVPKEY_DeviceInterface_ReferenceString = new DEVPROPKEY(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 5);

        private enum DEVPROPTYPE
        {
            DEVPROP_TYPE_EMPTY = 0x00000000,
            DEVPROP_TYPE_NULL = 0x00000001,
            DEVPROP_TYPE_SBYTE = 0x00000002,
            DEVPROP_TYPE_BYTE = 0x00000003,
            DEVPROP_TYPE_INT16 = 0x00000004,
            DEVPROP_TYPE_UINT16 = 0x00000005,
            DEVPROP_TYPE_INT32 = 0x00000006,
            DEVPROP_TYPE_UINT32 = 0x00000007,
            DEVPROP_TYPE_INT64 = 0x00000008,
            DEVPROP_TYPE_UINT64 = 0x00000009,
            DEVPROP_TYPE_FLOAT = 0x0000000A,
            DEVPROP_TYPE_DOUBLE = 0x0000000B,
            DEVPROP_TYPE_DECIMAL = 0x0000000C,
            DEVPROP_TYPE_GUID = 0x0000000D,
            DEVPROP_TYPE_CURRENCY = 0x0000000E,
            DEVPROP_TYPE_DATE = 0x0000000F,
            DEVPROP_TYPE_FILETIME = 0x00000010,
            DEVPROP_TYPE_BOOLEAN = 0x00000011,
            DEVPROP_TYPE_STRING = 0x00000012,
            DEVPROP_TYPE_STRING_LIST = DEVPROP_TYPE_STRING | DEVPROP_TYPEMOD_LIST,
            DEVPROP_TYPE_SECURITY_DESCRIPTOR = 0x00000013,
            DEVPROP_TYPE_SECURITY_DESCRIPTOR_STRING = 0x00000014,
            DEVPROP_TYPE_DEVPROPKEY = 0x00000015,
            DEVPROP_TYPE_DEVPROPTYPE = 0x00000016,
            DEVPROP_TYPE_BINARY = DEVPROP_TYPE_BYTE | DEVPROP_TYPEMOD_ARRAY,
            DEVPROP_TYPE_ERROR = 0x00000017,
            DEVPROP_TYPE_NTSTATUS = 0x00000018,
            DEVPROP_TYPE_STRING_INDIRECT = 0x00000019,
        }
    }
}
