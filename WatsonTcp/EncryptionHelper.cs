using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WatsonTcp
{
    internal static class EncryptionHelper
    {
        internal static byte[] Encrypt<T>(byte[] data, byte[] key, byte[] salt)
            where T : SymmetricAlgorithm, new()
        {
            using (T algorithm = new T())
            {
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
        }

        internal static byte[] Decrypt<T>(byte[] data, byte[] key, byte[] salt)
            where T : SymmetricAlgorithm, new()
        {
            using (T algorithm = new T())
            {
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
        }
        
        internal static string Sha256Hash(string rawData)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                StringBuilder builder = new StringBuilder();

                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                
                return builder.ToString();
            }
        }
    }
}