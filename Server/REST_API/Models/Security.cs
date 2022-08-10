using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace REST_API.Models
{
    public static class Security
    {
        /// <summary>
        /// Defines the m_keysize
        /// </summary>
        private const int m_keysize = 256;

        /// <summary>
        /// Defines the m_derivationIterations
        /// </summary>
        private const int m_derivationIterations = 1000;

        public static string toCheck = "=EHKm@2r";

        private static readonly byte[] saltStringBytes = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9,
            10, 11, 12, 13, 14, 15, 16, 17, 18, 19,
            20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31 };
                 
        private static readonly byte[] ivStringBytes = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9,
            10, 11, 12, 13, 14, 15, 16, 17, 18, 19,
            20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31 };

        /// <summary>
        /// Encrypt a string by using a passPhrase and the Rijndael algorithm.
        /// </summary>
        /// <param name="plainText">the string to encrypt<see cref="string"/></param>
        /// <param name="passPhrase">the passPhrase to use<see cref="string"/></param>
        /// <returns><see cref="string"/>The encrypted string</returns>
        public static string Encrypt(string plainText, string passPhrase)
        {
            //var saltStringBytes = GenerateRandomBits();
            //var ivStringBytes = GenerateRandomBits();
            var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            using (var password = new Rfc2898DeriveBytes(passPhrase, saltStringBytes, m_derivationIterations))
            {
                var keyBytes = password.GetBytes(m_keysize / 8);
                using (var symmetricKey = new RijndaelManaged())
                {
                    symmetricKey.BlockSize = 256;
                    symmetricKey.Mode = CipherMode.CBC;
                    symmetricKey.Padding = PaddingMode.PKCS7;
                    using (var encryptor = symmetricKey.CreateEncryptor(keyBytes, ivStringBytes))
                    {
                        using (var memoryStream = new MemoryStream())
                        {
                            using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                            {
                                cryptoStream.Write(plainTextBytes, 0, plainTextBytes.Length);
                                cryptoStream.FlushFinalBlock();

                                byte[] cipherTextBytes = saltStringBytes;
                                cipherTextBytes = cipherTextBytes.Concat(ivStringBytes).ToArray();
                                cipherTextBytes = cipherTextBytes.Concat(memoryStream.ToArray()).ToArray();
                                memoryStream.Close();
                                cryptoStream.Close();
                                return Convert.ToBase64String(cipherTextBytes);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Decrypt a string that was previously encrypted using Rinjdael algorithm
        /// </summary>
        /// <param name="cipherText">the encrypted string<see cref="string"/></param>
        /// <param name="passPhrase">the key to decrypt<see cref="string"/></param>
        /// <returns><see cref="string"/>The decrypted string</returns>
        public static string Decrypt(string cipherText, string passPhrase)
        {
            byte[] cipherTextBytesWithSaltAndIv = Convert.FromBase64String(cipherText);
            byte[] saltStringBytes = cipherTextBytesWithSaltAndIv.Take(m_keysize / 8).ToArray();
            byte[] ivStringBytes = cipherTextBytesWithSaltAndIv.Skip(m_keysize / 8).Take(m_keysize / 8).ToArray();
            byte[] cipherTextBytes = cipherTextBytesWithSaltAndIv.Skip((m_keysize / 8) * 2).Take(cipherTextBytesWithSaltAndIv.Length - ((m_keysize / 8) * 2)).ToArray();

            using (var password = new Rfc2898DeriveBytes(passPhrase, saltStringBytes, m_derivationIterations))
            {
                byte[] keyBytes = password.GetBytes(m_keysize / 8);
                using (var symmetricKey = new RijndaelManaged())
                {
                    symmetricKey.BlockSize = 256;
                    symmetricKey.Mode = CipherMode.CBC;
                    symmetricKey.Padding = PaddingMode.PKCS7;
                    using (ICryptoTransform decryptor = symmetricKey.CreateDecryptor(keyBytes, ivStringBytes))
                    {
                        using (MemoryStream memoryStream = new MemoryStream(cipherTextBytes))
                        {
                            using (CryptoStream cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                            {
                                byte[] plainTextBytes = new byte[cipherTextBytes.Length];
                                int decryptedByteCount = cryptoStream.Read(plainTextBytes, 0, plainTextBytes.Length);
                                memoryStream.Close();
                                cryptoStream.Close();
                                return Encoding.UTF8.GetString(plainTextBytes, 0, decryptedByteCount);
                            }
                        }
                    }
                }
            }
        }
    }
}