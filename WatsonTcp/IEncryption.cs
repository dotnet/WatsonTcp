using System;

namespace WatsonTcp
{
    public interface IEncryption : IDisposable
    {
        byte[] Encrypt(byte[] data, byte[] key = null, byte[] salt = null);
        byte[] Decrypt(byte[] data, byte[] key = null, byte[] salt = null);
    }
}