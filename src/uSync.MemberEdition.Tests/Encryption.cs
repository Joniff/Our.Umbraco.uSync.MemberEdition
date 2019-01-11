using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace uSync.MemberEdition.Tests
{
	[TestClass]
	public class Encryption
	{

		private string RandomString(int length) 
		{
			char[] possibleCharsArray = "abcdefghijklmnopqrstuvwxyz0123456789 _-+,.!\"£$%^&*(){}[]@".ToCharArray();
			Random random = new Random();

			return new string(
				Enumerable.Repeat(possibleCharsArray, length)
					.Select(s => s[random.Next(s.Length)])
					.ToArray());
		}

		[TestMethod]
		public void DecryptUsingSalt()
		{
			for (int count = 0; count != 10000; count++)
			{
				var encryption = new Security.Cryptography(RandomString(count + 1));
				var text = RandomString(count + 1);
				var code = encryption.Encrypt(text);
				
				var text2 = encryption.Decrypt(code);

				Assert.AreEqual(text, text2);
			}
		}

		[TestMethod]
		public void DecryptSanSalt()
		{
			for (int count = 0; count != 10000; count++)
			{
				var encryption = new Security.Cryptography();
				var text = RandomString(count + 1);
				var code = encryption.Encrypt(text);
				
				var text2 = encryption.Decrypt(code);

				Assert.AreEqual(text, text2);
			}
		}

		[TestMethod]
		public void DecryptUsingMultipleClasses()
		{
			for (int count = 0; count != 10000; count++)
			{
				var salt = RandomString(count + 1);
				var encryption = new Security.Cryptography(salt);
				var text = RandomString(count + 1);
				var code = encryption.Encrypt(text);
				
				var encryption2 = new Security.Cryptography(salt);
				var text2 = encryption2.Decrypt(code);

				Assert.AreEqual(text, text2);
			}
		}

		[TestMethod]
		public void DecryptTest()
		{
			var salt = "icclowelondon@gmail.comicc dev icc dev";
			var encryption = new Security.Cryptography(salt);
			var text = "Z8N3i+7z/TEfJM91R49ojOuHKbc=";
			var code = encryption.Encrypt(text);
				
			var encryption2 = new Security.Cryptography(salt);
			var text2 = encryption2.Decrypt("AUW/N6bZ18aNzOyLy+ynzDE6lsNkp+vehPeSgtVMHTo=");

			Assert.AreEqual(text, text2);
		}

	}
}
