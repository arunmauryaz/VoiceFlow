using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VoiceFlow.Helpers;

public static class SecureStorageHelper
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VoiceFlow.Secure.Salt.v1");

    public static string EncryptString(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encryptedBytes);
    }

    public static string? DecryptString(string cipherBase64)
    {
        if (string.IsNullOrWhiteSpace(cipherBase64))
            return null;

        try
        {
            byte[] cipherBytes = Convert.FromBase64String(cipherBase64);
            byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return null;
        }
    }

    public static void SaveSecureFile(string filePath, string secret)
    {
        string encrypted = EncryptString(secret);
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(filePath, encrypted);
    }

    public static string? LoadSecureFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        string encrypted = File.ReadAllText(filePath);
        return DecryptString(encrypted);
    }
}
