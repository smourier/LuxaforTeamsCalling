using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace LuxaforTeamsCalling
{
    public class DeviceInterface
    {
        private static readonly Lazy<IReadOnlyDictionary<Guid, string>> _guidsNames = new Lazy<IReadOnlyDictionary<Guid, string>>(GetGuidsNames);
        public static IReadOnlyDictionary<Guid, string> GuidsNames => _guidsNames.Value;

        private static readonly Lazy<IReadOnlyList<Guid>> _registryClassGuids = new Lazy<IReadOnlyList<Guid>>(GetRegistryClassGuids, true);
        public static IReadOnlyList<Guid> RegistryClassGuids => _registryClassGuids.Value;

        private readonly Dictionary<DEVPROPKEY, object> _properties;

        public DeviceInterface(Dictionary<DEVPROPKEY, object> properties, string path)
        {
            if (properties == null)
                throw new ArgumentNullException(nameof(properties));

            if (path == null)
                throw new ArgumentNullException(nameof(path));

            _properties = properties;
            Path = path;
        }

        public IReadOnlyDictionary<DEVPROPKEY, object> Properties => _properties;

        public T GetPropertyValue<T>(DEVPROPKEY pk, T defaultValue = default)
        {
            if (_properties.TryGetValue(pk, out var value))
                return (T)value;

            return defaultValue;
        }

        public virtual string Path { get; }
        public virtual string FriendlyName => GetPropertyValue<string>(DEVPKEY_DeviceInterface_FriendlyName);
        public virtual string DeviceInstanceId => GetPropertyValue<string>(Device.DEVPKEY_Device_InstanceId);
        public virtual Guid ClassGuid => GetPropertyValue<Guid>(DEVPKEY_DeviceInterface_ClassGuid);
        public virtual bool IsEnabled => GetPropertyValue<bool>(DEVPKEY_DeviceInterface_Enabled);
        public virtual ushort HidProductId => GetPropertyValue<ushort>(DEVPKEY_DeviceInterface_HID_ProductId);
        public virtual ushort HidVendorId => GetPropertyValue<ushort>(DEVPKEY_DeviceInterface_HID_VendorId);

        public override string ToString() => FriendlyName;

        private static IReadOnlyList<Guid> GetRegistryClassGuids()
        {
            var list = new List<Guid>();
            using (var reg = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceClasses", false))
            {
                if (reg != null)
                {
                    foreach (var name in reg.GetSubKeyNames())
                    {
                        if (Guid.TryParse(name, out var guid))
                        {
                            list.Add(guid);
                        }
                    }
                }
            }
            return list.ToArray();
        }

        private static IReadOnlyDictionary<Guid, string> GetGuidsNames()
        {
            var dic = new ConcurrentDictionary<Guid, string>();
            foreach (var field in typeof(DeviceInterface).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.Name.StartsWith("GUID_DEVINTERFACE_")))
            {
                var guid = (Guid)field.GetValue(null);
                dic[guid] = field.Name;
            }
            return dic;
        }

        public static readonly Guid GUID_DEVINTERFACE_A2DP_SIDEBAND_AUDIO = new Guid("f3b1362f-c9f4-4dd1-9d55-e02038a129fb");
        public static readonly Guid GUID_DEVINTERFACE_ASP_INFRA_DEVICE = new Guid("ff823995-7a72-4c80-8757-c67ee13d1a49");
        public static readonly Guid GUID_DEVINTERFACE_BIOMETRIC_READER = new Guid("e2b5183a-99ea-4cc3-ad6b-80ca8d715b80");
        public static readonly Guid GUID_DEVINTERFACE_BLUETOOTH_HFP_SCO_HCIBYPASS = new Guid("be446647-f655-4919-8bd0-125ba5d4ce65");
        public static readonly Guid GUID_DEVINTERFACE_BRIGHTNESS = new Guid("fde5bba4-b3f9-46fb-bdaa-0728ce3100b4");
        public static readonly Guid GUID_DEVINTERFACE_BRIGHTNESS_2 = new Guid("148a3c98-0ecd-465a-b634-b05f195f7739");
        public static readonly Guid GUID_DEVINTERFACE_BRIGHTNESS_3 = new Guid("197a4a6e-0391-4322-96ea-c2760f881d3a");
        public static readonly Guid GUID_DEVINTERFACE_CDCHANGER = new Guid("53f56312-b6bf-11d0-94f2-00a0c91efb8b");
        public static readonly Guid GUID_DEVINTERFACE_CDROM = new Guid("53f56308-b6bf-11d0-94f2-00a0c91efb8b");
        public static readonly Guid GUID_DEVINTERFACE_CHARGING_ARBITRATION = new Guid("ec0a1cc9-4294-43fb-bf37-b850ce95f337");
        public static readonly Guid GUID_DEVINTERFACE_COMPORT = new Guid("86e0d1e0-8089-11d0-9ce4-08003e301f73");
        public static readonly Guid GUID_DEVINTERFACE_CONFIGURABLE_USBFN_CHARGER = new Guid("7158c35c-c1bc-4d90-acb1-8020bd0e19ca");
        public static readonly Guid GUID_DEVINTERFACE_CONFIGURABLE_WIRELESS_CHARGER = new Guid("3612b1c8-3633-47d3-8af5-00a4dfa04793");
        public static readonly Guid GUID_DEVINTERFACE_DIRECTLY_ASSIGNABLE_DEVICE = new Guid("0db3e0f9-3536-4213-9572-ad77e224be27");
        public static readonly Guid GUID_DEVINTERFACE_DISK = new Guid("53f56307-b6bf-11d0-94f2-00a0c91efb8b");
        public static readonly Guid GUID_DEVINTERFACE_DISPLAY_ADAPTER = new Guid("5b45201d-f2f2-4f3b-85bb-30ff1f953599");
        public static readonly Guid GUID_DEVINTERFACE_DMP = new Guid("25b4e268-2a05-496e-803b-266837fbda4b");
        public static readonly Guid GUID_DEVINTERFACE_DMR = new Guid("d0875fb4-2196-4c7a-a63d-e416addd60a1");
        public static readonly Guid GUID_DEVINTERFACE_DMS = new Guid("c96037ae-a558-4470-b432-115a31b85553");
        public static readonly Guid GUID_DEVINTERFACE_EMMC_PARTITION_ACCESS_GPP = new Guid("2e0e2e39-1f19-4595-a906-887882e73903");
        public static readonly Guid GUID_DEVINTERFACE_EMMC_PARTITION_ACCESS_RPMB = new Guid("27447c21-bcc3-4d07-a05b-a3395bb4eee7");
        public static readonly Guid GUID_DEVINTERFACE_ENHANCED_STORAGE_SILO = new Guid("3897f6a4-fd35-4bc8-a0b7-5dbba36adafa");
        public static readonly Guid GUID_DEVINTERFACE_FLOPPY = new Guid("53f56311-b6bf-11d0-94f2-00a0c91efb8b");
        public static readonly Guid GUID_DEVINTERFACE_GNSS = new Guid("3336e5e4-018a-4669-84c5-bd05f3bd368b");
        public static readonly Guid GUID_DEVINTERFACE_GRAPHICSPOWER = new Guid("ea5c6870-e93c-4588-bef1-fec42fc9429a");
        public static readonly Guid GUID_DEVINTERFACE_HID = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");
        public static readonly Guid GUID_DEVINTERFACE_HIDDEN_VOLUME = new Guid("7f108a28-9833-4b3b-b780-2c6b5fa5c062");
        public static readonly Guid GUID_DEVINTERFACE_HOLOGRAPHIC_DISPLAY = new Guid("deac60ab-66e2-42a4-ad9b-557ee33ae2d5");
        public static readonly Guid GUID_DEVINTERFACE_HPMI = new Guid("dedae202-1d20-4c40-a6f3-1897e319d54f");
        public static readonly Guid GUID_DEVINTERFACE_I2C = new Guid("2564aa4f-dddb-4495-b497-6ad4a84163d7");
        public static readonly Guid GUID_DEVINTERFACE_IMAGE = new Guid("6bdd1fc6-810f-11d0-bec7-08002be2092f");
        public static readonly Guid GUID_DEVINTERFACE_IPPUSB_PRINT = new Guid("f2f40381-f46d-4e51-bce7-62de6cf2d098");
        public static readonly Guid GUID_DEVINTERFACE_KEYBOARD = new Guid("884b96c3-56ef-11d1-bc8c-00a0c91405dd");
        public static readonly Guid GUID_DEVINTERFACE_LAMP = new Guid("6c11e9e3-8238-4f0a-0a19-aaec26ca5e98");
        public static readonly Guid GUID_DEVINTERFACE_MEDIUMCHANGER = new Guid("53f56310-b6bf-11d0-94f2-00a0c91efb8b");
        public static readonly Guid GUID_DEVINTERFACE_MIRACAST_DISPLAY = new Guid("af03f190-22af-48cb-94bb-b78e76a25107");
        public static readonly Guid GUID_DEVINTERFACE_MIRACAST_DISPLAY_ARRIVAL = new Guid("64f1f453-d465-4097-b8f8-cdff171fc335");
        public static readonly Guid GUID_DEVINTERFACE_MODEM = new Guid("2c7089aa-2e0e-11d1-b114-00c04fc2aae4");
        public static readonly Guid GUID_DEVINTERFACE_MONITOR = new Guid("e6f07b5f-ee97-4a90-b076-33f57bf4eaa7");
        public static readonly Guid GUID_DEVINTERFACE_MOUSE = new Guid("378de44c-56ef-11d1-bc8c-00a0c91405dd");
        public static readonly Guid GUID_DEVINTERFACE_NET = new Guid("cac88484-7515-4c03-82e6-71a87abac361");
        public static readonly Guid GUID_DEVINTERFACE_NETUIO = new Guid("08336f60-0679-4c6c-85d2-ae7ced65fff7");
        public static readonly Guid GUID_DEVINTERFACE_NFCDTA = new Guid("7fd3f30b-5e49-4be1-b3aa-af06260d236a");
        public static readonly Guid GUID_DEVINTERFACE_NFCSE = new Guid("8dc7c854-f5e5-4bed-815d-0c85ad047725");
        public static readonly Guid GUID_DEVINTERFACE_NFP = new Guid("fb3842cd-9e2a-4f83-8fcc-4b0761139ae9");
        public static readonly Guid GUID_DEVINTERFACE_OPM = new Guid("bf4672de-6b4e-4be4-a325-68a91ea49c09");
        public static readonly Guid GUID_DEVINTERFACE_OPM_2 = new Guid("7f098726-2ebb-4ff3-a27f-1046b95dc517");
        public static readonly Guid GUID_DEVINTERFACE_OPM_2_JTP = new Guid("e929eea4-b9f1-407b-aab9-ab08bb44fbf4");
        public static readonly Guid GUID_DEVINTERFACE_OPM_3 = new Guid("693a2cb1-8c8d-4ab6-9555-4b85ef2c7c6b");
        public static readonly Guid GUID_DEVINTERFACE_PARALLEL = new Guid("97f76ef0-f883-11d0-af1f-0000f800845c");
        public static readonly Guid GUID_DEVINTERFACE_PARCLASS = new Guid("811fc6a5-f728-11d0-a537-0000f8753ed1");
        public static readonly Guid GUID_DEVINTERFACE_PARTITION = new Guid("53f5630a-b6bf-11d0-94f2-00a0c91efb8b");
        public static readonly Guid GUID_DEVINTERFACE_POS_CASHDRAWER = new Guid("772e18f2-8925-4229-a5ac-6453cb482fda");
        public static readonly Guid GUID_DEVINTERFACE_POS_LINEDISPLAY = new Guid("4fc9541c-0fe6-4480-a4f6-9495a0d17cd2");
        public static readonly Guid GUID_DEVINTERFACE_POS_MSR = new Guid("2a9fe532-0cdc-44f9-9827-76192f2ca2fb");
        public static readonly Guid GUID_DEVINTERFACE_POS_PRINTER = new Guid("c7bc9b22-21f0-4f0d-9bb6-66c229b8cd33");
        public static readonly Guid GUID_DEVINTERFACE_POS_SCANNER = new Guid("c243ffbd-3afc-45e9-b3d3-2ba18bc7ebc5");
        public static readonly Guid GUID_DEVINTERFACE_PWM_CONTROLLER = new Guid("60824b4c-eed1-4c9c-b49c-1b961461a819");
        public static readonly Guid GUID_DEVINTERFACE_PWM_CONTROLLER_WSZ = new Guid("{60824B4C-EED1-4C9C-B49C-1B961461A819}");
        public static readonly Guid GUID_DEVINTERFACE_SCM_PHYSICAL_DEVICE = new Guid("4283609d-4dc2-43be-bbb4-4f15dfce2c61");
        public static readonly Guid GUID_DEVINTERFACE_SENSOR = new Guid("ba1bb692-9b7a-4833-9a1e-525ed134e7e2");
        public static readonly Guid GUID_DEVINTERFACE_SERENUM_BUS_ENUMERATOR = new Guid("4d36e978-e325-11ce-bfc1-08002be10318");
        public static readonly Guid GUID_DEVINTERFACE_SERVICE_VOLUME = new Guid("6ead3d82-25ec-46bc-b7fd-c1f0df8f5037");
        public static readonly Guid GUID_DEVINTERFACE_SES = new Guid("1790c9ec-47d5-4df3-b5af-9adf3cf23e48");
        public static readonly Guid GUID_DEVINTERFACE_SIDESHOW = new Guid("152e5811-feb9-4b00-90f4-d32947ae1681");
        public static readonly Guid GUID_DEVINTERFACE_SMARTCARD_READER = new Guid("50dd5230-ba8a-11d1-bf5d-0000f805f530");
        public static readonly Guid GUID_DEVINTERFACE_STORAGEPORT = new Guid("2accfe60-c130-11d2-b082-00a0c91efb8b");
        public static readonly Guid GUID_DEVINTERFACE_SURFACE_VIRTUAL_DRIVE = new Guid("2e34d650-5819-42ca-84ae-d30803bae505");
        public static readonly Guid GUID_DEVINTERFACE_TAPE = new Guid("53f5630b-b6bf-11d0-94f2-00a0c91efb8b");
        public static readonly Guid GUID_DEVINTERFACE_THERMAL_COOLING = new Guid("dbe4373d-3c81-40cb-ace4-e0e5d05f0c9f");
        public static readonly Guid GUID_DEVINTERFACE_THERMAL_MANAGER = new Guid("927ec093-69a4-4bc0-bd02-711664714463");
        public static readonly Guid GUID_DEVINTERFACE_UNIFIED_ACCESS_RPMB = new Guid("27447c21-bcc3-4d07-a05b-a3395bb4eee7");
        public static readonly Guid GUID_DEVINTERFACE_USB_BILLBOARD = new Guid("5e9adaef-f879-473f-b807-4e5ea77d1b1c");
        public static readonly Guid GUID_DEVINTERFACE_USB_DEVICE = new Guid("a5dcbf10-6530-11d2-901f-00c04fb951ed");
        public static readonly Guid GUID_DEVINTERFACE_USB_HOST_CONTROLLER = new Guid("3abf6f2d-71c4-462a-8a92-1e6861e6af27");
        public static readonly Guid GUID_DEVINTERFACE_USB_HUB = new Guid("f18a0e88-c30c-11d0-8815-00a0c906bed8");
        public static readonly Guid GUID_DEVINTERFACE_USB_SIDEBAND_AUDIO_HS_HCIBYPASS = new Guid("02baa4b5-33b5-4d97-ae4f-e86dde17536f");
        public static readonly Guid GUID_DEVINTERFACE_USBPRINT = new Guid("28d78fad-5a12-11d1-ae5b-0000f803a8c2");
        public static readonly Guid GUID_DEVINTERFACE_VIDEO_OUTPUT_ARRIVAL = new Guid("1ad9e4f0-f88d-4360-bab9-4c2d55e564cd");
        public static readonly Guid GUID_DEVINTERFACE_VIRTUALIZABLE_DEVICE = new Guid("a13a7a93-11f0-4bd2-a9f5-6b5c5b88527d");
        public static readonly Guid GUID_DEVINTERFACE_VM_GENCOUNTER = new Guid("3ff2c92b-6598-4e60-8e1c-0ccf4927e319");
        public static readonly Guid GUID_DEVINTERFACE_VMLUN = new Guid("6f416619-9f29-42a5-b20b-37e219ca02b0");
        public static readonly Guid GUID_DEVINTERFACE_VOLUME = new Guid("53f5630d-b6bf-11d0-94f2-00a0c91efb8b");
        public static readonly Guid GUID_DEVINTERFACE_VPCI = new Guid("57863182-c948-4692-97e3-34b57662a3e0");
        public static readonly Guid GUID_DEVINTERFACE_WDDM3_ON_VB = new Guid("e922004d-eb9c-4de1-9224-a9ceaa959bce");
        public static readonly Guid GUID_DEVINTERFACE_WIFIDIRECT_DEVICE = new Guid("439b20af-8955-405b-99f0-a62af0c68d43");
        public static readonly Guid GUID_DEVINTERFACE_WPD = new Guid("6ac27878-a6fa-4155-ba85-f98f491d4f33");
        public static readonly Guid GUID_DEVINTERFACE_WPD_DRIVER_PREPARED = new Guid("10497b1b-ba51-44e5-8318-a65c837b6661");
        public static readonly Guid GUID_DEVINTERFACE_WPD_PRIVATE = new Guid("ba0c718f-4ded-49b7-bdd3-fabe28661211");
        public static readonly Guid GUID_DEVINTERFACE_WPD_SERVICE = new Guid("9ef44f80-3d64-4246-a6aa-206f328d1edc");
        public static readonly Guid GUID_DEVINTERFACE_WRITEONCEDISK = new Guid("53f5630c-b6bf-11d0-94f2-00a0c91efb8b");
        public static readonly Guid GUID_DEVINTERFACE_WWAN_CONTROLLER = new Guid("669159fd-e3c0-45cb-bc5f-95995bcd06cd");
        public static readonly Guid GUID_DEVINTERFACE_ZNSDISK = new Guid("b87941c5-ffdb-43c7-b6b1-20b632f0b109");

        private static readonly Guid FMTID_DeviceInterface = new Guid("026e516e-b814-414b-83cd-856d6fef4822");
        private static readonly DEVPROPKEY DEVPKEY_DeviceInterface_FriendlyName = new DEVPROPKEY(FMTID_DeviceInterface, 2);
        internal static readonly DEVPROPKEY DEVPKEY_DeviceInterface_ClassGuid = new DEVPROPKEY(FMTID_DeviceInterface, 4);
        internal static readonly DEVPROPKEY DEVPKEY_DeviceInterface_Enabled = new DEVPROPKEY(FMTID_DeviceInterface, 3);

        private static readonly Guid FMTID_DeviceInterfaceHID = new Guid("cbf38310-4a17-4310-a1eb-247f0b67593b");
        private static readonly DEVPROPKEY DEVPKEY_DeviceInterface_HID_VendorId = new DEVPROPKEY(FMTID_DeviceInterfaceHID, 5);
        private static readonly DEVPROPKEY DEVPKEY_DeviceInterface_HID_ProductId = new DEVPROPKEY(FMTID_DeviceInterfaceHID, 6);
    }
}
