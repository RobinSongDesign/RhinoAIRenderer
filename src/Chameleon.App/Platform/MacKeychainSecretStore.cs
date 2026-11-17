using System;
using System.Runtime.InteropServices;
using System.Text;
using Chameleon.Core.Settings;

namespace Chameleon.App.Platform
{
    /// <summary>Secrets stored as generic passwords in the user's macOS login keychain. macOS only.</summary>
    public sealed class MacKeychainSecretStore : ISecretStore
    {
        /// <summary>Keychain service name under which every Chameleon secret is filed.</summary>
        public const string ServiceName = "com.robinsongdesign.chameleon";

        private const string SecurityLibrary = "/System/Library/Frameworks/Security.framework/Security";
        private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const int ErrSecSuccess = 0;
        private const int ErrSecItemNotFound = -25300;

        private readonly byte[] _service = Encoding.UTF8.GetBytes(ServiceName);

        /// <inheritdoc />
        public string? Get(string name)
        {
            byte[] account = Encoding.UTF8.GetBytes(name);
            int status = SecKeychainFindGenericPassword(
                IntPtr.Zero, (uint)_service.Length, _service, (uint)account.Length, account,
                out uint length, out IntPtr data, out IntPtr item);

            if (status != ErrSecSuccess)
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
                Release(item);
            }
        }

        /// <inheritdoc />
        public void Set(string name, string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            byte[] account = Encoding.UTF8.GetBytes(name);
            byte[] secret = Encoding.UTF8.GetBytes(value);

            int status = SecKeychainFindGenericPassword(
                IntPtr.Zero, (uint)_service.Length, _service, (uint)account.Length, account,
                out _, out IntPtr existingData, out IntPtr item);

            if (status == ErrSecSuccess)
            {
                try
                {
                    SecKeychainItemFreeContent(IntPtr.Zero, existingData);
                    Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)secret.Length, secret));
                }
                finally
                {
                    Release(item);
                }

                return;
            }

            if (status != ErrSecItemNotFound)
            {
                Check(status);
            }

            Check(SecKeychainAddGenericPassword(
                IntPtr.Zero, (uint)_service.Length, _service, (uint)account.Length, account,
                (uint)secret.Length, secret, IntPtr.Zero));
        }

        /// <inheritdoc />
        public void Delete(string name)
        {
            byte[] account = Encoding.UTF8.GetBytes(name);
            int status = SecKeychainFindGenericPassword(
                IntPtr.Zero, (uint)_service.Length, _service, (uint)account.Length, account,
                out _, out IntPtr data, out IntPtr item);

            if (status != ErrSecSuccess)
            {
                return;
            }

            try
            {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
                SecKeychainItemDelete(item);
            }
            finally
            {
                Release(item);
            }
        }

        /// <inheritdoc />
        public bool Contains(string name)
        {
            return Get(name) != null;
        }

        private static void Check(int status)
        {
            if (status != ErrSecSuccess)
            {
                throw new InvalidOperationException("Keychain operation failed with OSStatus " + status + ".");
            }
        }

        private static void Release(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                CFRelease(handle);
            }
        }

        [DllImport(SecurityLibrary)]
        private static extern int SecKeychainAddGenericPassword(
            IntPtr keychain,
            uint serviceNameLength, byte[] serviceName,
            uint accountNameLength, byte[] accountName,
            uint passwordLength, byte[] passwordData,
            IntPtr itemRef);

        [DllImport(SecurityLibrary)]
        private static extern int SecKeychainFindGenericPassword(
            IntPtr keychainOrArray,
            uint serviceNameLength, byte[] serviceName,
            uint accountNameLength, byte[] accountName,
            out uint passwordLength, out IntPtr passwordData,
            out IntPtr itemRef);

        [DllImport(SecurityLibrary)]
        private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

        [DllImport(SecurityLibrary)]
        private static extern int SecKeychainItemDelete(IntPtr itemRef);

        [DllImport(SecurityLibrary)]
        private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

        [DllImport(CoreFoundationLibrary)]
        private static extern void CFRelease(IntPtr cf);
    }
}
