using InSalute.Core.Dispatching;
using Xunit;

namespace InSalute.Tests;

public class PasswordGeneratorTests
{
    [Fact]
    public void Generated_passwords_have_length_12_and_only_alphabet_characters()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = PasswordGenerator.Generate();
            Assert.Equal(12, password.Length);
            Assert.All(password, c => Assert.Contains(c, PasswordGenerator.Alphabet));
        }
    }

    [Fact]
    public void Generated_passwords_contain_upper_lower_and_digit()
    {
        for (var i = 0; i < 1000; i++)
        {
            var password = PasswordGenerator.Generate();
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
        }
    }

    [Fact]
    public void A_thousand_generated_passwords_are_all_different()
    {
        var passwords = Enumerable.Range(0, 1000).Select(_ => PasswordGenerator.Generate()).ToHashSet();
        Assert.Equal(1000, passwords.Count);
    }
}
