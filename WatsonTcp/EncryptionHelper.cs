using System.IO;
using System.Security.Cryptography;

namespace WatsonTcp
{
    internal static class EncryptionHelper
    {
        public static byte[] Encrypt<T>(byte[] data, byte[] key, byte[] salt)
            where T : SymmetricAlgorithm, new()
        {
            T algorithm = new T();

            Rfc2898DeriveBytes rgb = new Rfc2898DeriveBytes(key, salt, 1000);
            byte[] rgbKey = rgb.GetBytes(algorithm.KeySize >> 3);
            byte[] rgbIV = rgb.GetBytes(algorithm.BlockSize >> 3);

            ICryptoTransform transform = algorithm.CreateEncryptor(rgbKey, rgbIV);

            using (MemoryStream ms = new MemoryStream())
            {
                using (CryptoStream cs = new CryptoStream(ms, transform, CryptoStreamMode.Write))
                {
                    cs.Write(data, 0, data.Length);
                }

                return ms.ToArray();
            }
        }

        public static byte[] Decrypt<T>(byte[] data, byte[] key, byte[] salt)
            where T : SymmetricAlgorithm, new()
        {
            T algorithm = new T();

            Rfc2898DeriveBytes rgb = new Rfc2898DeriveBytes(key, salt, 1000);
            byte[] rgbKey = rgb.GetBytes(algorithm.KeySize >> 3);
            byte[] rgbIV = rgb.GetBytes(algorithm.BlockSize >> 3);

            ICryptoTransform transform = algorithm.CreateDecryptor(rgbKey, rgbIV);

            using (CryptoStream cs = new CryptoStream(new MemoryStream(data), transform, CryptoStreamMode.Read))
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    cs.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }
        
        public static byte[] Xor(byte[] key, byte[] data)
        {
            int n1 = 11;
            int n2 = 13;
            int ns = 257;

            for (int i = 0; i <= key.Length - 1; i++)
            {
                ns += ns % (key[i] + 1);
            }

            byte[] b = new byte[data.Length];
            for (int i = 0; i <= data.Length - 1; i++)
            {
                ns = key[i % key.Length] + ns;
                n1 = (ns + 5) * (n1 & 255) + (n1 >> 8);
                n2 = (ns + 7) * (n2 & 255) + (n2 >> 8);
                ns = ((n1 << 8) + n2) & 255;

                b[i] = (byte)(data[i] ^ (byte)ns);
            }

            return b;
        }
    }
}