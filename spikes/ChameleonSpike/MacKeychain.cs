using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ChameleonSpike
{
    /// <summary>Minimal generic-password access to the macOS login keychain, for check E2.</summary>
    public static class MacKeychain
    {
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        /// <summary>Adds a password; returns the OSStatus (0 = success).</summary>
        public static int Add(string service, string account, string secret)
        {
            byte[] s = Encoding.UTF8.GetBytes(service);
            byte[] a = Encoding.UTF8.GetBytes(account);
            byte[] p = Encoding.UTF8.GetBytes(secret);
            return SecKeychainAddGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a, (uint)p.Length, p, IntPtr.Zero);
        }

        /// <summary>Reads a password; returns null when it does not exist.</summary>
        public static string? Find(string service, string account, out int status)
        {
            byte[] s = Encoding.UTF8.GetBytes(service);
            byte[] a = Encoding.UTF8.GetBytes(account);
            status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a, out uint length, out IntPtr data, out IntPtr item);
            if (status != 0)
            {
                return null;
            }

            try
            {
                var bytes = new byte[length];
                if (length > 0)
                {
                    Marshal.Copy(data, bytes, 0, (int)length);
                }

                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
                if (item != IntPtr.Zero)
                {
                    CFRelease(item);
                }
            }
        }

        /// <summary>Deletes a password; returns the OSStatus (0 = success).</summary>
        public static int Delete(string service, string account)
        {
            byte[] s = Encoding.UTF8.GetBytes(service);
            byte[] a = Encoding.UTF8.GetBytes(account);
            int status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a, out _, out IntPtr data, out IntPtr item);
            if (status != 0)
            {
                return status;
            }

            try
            {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
                return SecKeychainItemDelete(item);
            }
            finally
            {
                if (item != IntPtr.Zero)
                {
                    CFRelease(item);
                }
            }
        }

        [DllImport(Security)]
        private static extern int SecKeychainAddGenericPassword(
            IntPtr keychain, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
            uint passwordLength, byte[] passwordData, IntPtr itemRef);

        [DllImport(Security)]
        private static extern int SecKeychainFindGenericPassword(
            IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
            out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

        [DllImport(Security)]
        private static extern int SecKeychainItemDelete(IntPtr itemRef);

        [DllImport(Security)]
        private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

        [DllImport(CoreFoundation)]
        private static extern void CFRelease(IntPtr cf);
    }
}
