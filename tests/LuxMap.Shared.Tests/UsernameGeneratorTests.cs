using System.Text;
using LuxMap.Modules.Identity.Accounts;
using LuxMap.Shared.Http;

namespace LuxMap.Shared.Tests;

public class UsernameGeneratorTests
{
    [Theory]
    [InlineData("Nguyễn Hữu Mỹ", "mynh")]
    [InlineData("Đỗ Văn Đức", "ducdv")]
    [InlineData("Trần Thị Bích Ngọc", "ngocttb")]
    [InlineData("Lê  Văn-Tám", "tamlv")]
    [InlineData("Mỹ", "my")]
    [InlineData("  Lê\tVăn--Tám  ", "tamlv")]
    public void The_last_word_precedes_all_earlier_initials(string name, string expected)
    {
        Assert.Equal(expected, UsernameGenerator.BaseName(name));
        Assert.Equal(expected, UsernameGenerator.BaseName(name.Normalize(NormalizationForm.FormD)));
    }

    [Theory]
    [InlineData("王 Mỹ")]
    [InlineData("Mỹ2")]
    [InlineData("Анна")]
    [InlineData("Mỹ!")]
    [InlineData("---")]
    [InlineData(" \t ")]
    public void Unsupported_characters_or_an_empty_base_are_validation_errors(string name)
        => Assert.Throws<LuxMapException>(() => UsernameGenerator.BaseName(name));
}
