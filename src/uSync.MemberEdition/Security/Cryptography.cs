using System;
using System.Text;
using System.Security.Cryptography;
using System.IO;
using System.Linq;

namespace uSync.MemberEdition.Security
{
	public class Cryptography
	{
		private const int Keysize = 24;

		private byte[] GlobalSalt = 
		{
			0x77, 0x41, 0x46, 0x6B, 0x64, 0x34, 0x37, 0x73, 0x41, 0x38, 0x72, 0x6A, 0x50, 0x6E, 0x79, 0x51, 0x32, 0x4C, 0x4D, 0x72, 0x79, 0x67, 0x67, 0x34
		};

		private byte[] Salt;

		public Cryptography()
		{
			Salt = GlobalSalt;
		}

		public Cryptography(string salt)
		{
			Salt = new byte[Keysize];
			byte[] join = new byte[GlobalSalt.Length + salt.Length];
			System.Buffer.BlockCopy(GlobalSalt, 0, join, 0, GlobalSalt.Length);
			System.Buffer.BlockCopy(UTF8Encoding.UTF8.GetBytes(salt), 0, join, GlobalSalt.Length, salt.Length);
			MD5CryptoServiceProvider hashmd5 = new MD5CryptoServiceProvider();
			var hash = hashmd5.ComputeHash(join);
			for (int index = 0; index != Keysize; index++)
			{
				Salt[index] = (index < hash.Length) ? (byte) (hash[index] ^ GlobalSalt[index]) : GlobalSalt[index];
			}
		}

		public string Encrypt(string toEncrypt)
		{
			try
			{
				byte[] toEncryptArray = UTF8Encoding.UTF8.GetBytes(toEncrypt);

				using (TripleDESCryptoServiceProvider tdes = new TripleDESCryptoServiceProvider())
				{
					tdes.Key = Salt;
					tdes.Mode = CipherMode.ECB;
					tdes.Padding = PaddingMode.PKCS7;

					ICryptoTransform cTransform = tdes.CreateEncryptor();
					byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);

					return Convert.ToBase64String(resultArray, 0, resultArray.Length);
				}
			}
			catch (Exception ex)
			{
			}
			return null;
		}

		public string Decrypt(string toDecrypt)
		{
			try
			{
				byte[] toEncryptArray = Convert.FromBase64String(toDecrypt);

				using (TripleDESCryptoServiceProvider tdes = new TripleDESCryptoServiceProvider())
				{
					tdes.Key = Salt;
					tdes.Mode = CipherMode.ECB;
					tdes.Padding = PaddingMode.PKCS7;

					ICryptoTransform cTransform = tdes.CreateDecryptor();
					byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);

					return UTF8Encoding.UTF8.GetString(resultArray);
				}
			}
			catch (Exception ex)
			{
			}
			return null;
		}

	}
}
