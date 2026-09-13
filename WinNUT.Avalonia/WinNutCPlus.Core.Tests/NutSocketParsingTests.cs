using WinNutCPlus.Core.Models;
using WinNutCPlus.Core.Protocol;

namespace WinNutCPlus.Core.Tests;

public class NutSocketParsingTests
{
    [Theory]
    [InlineData("OK", NutResponseType.Ok)]
    [InlineData("VAR ups varname \"value\"", NutResponseType.Ok)]
    [InlineData("DESC ups varname \"desc\"", NutResponseType.Ok)]
    [InlineData("UPS myups \"description\"", NutResponseType.Ok)]
    [InlineData("BEGIN LIST VAR ups", NutResponseType.BeginList)]
    [InlineData("END LIST VAR ups", NutResponseType.EndList)]
    [InlineData("Network UPS Tools upsd 2.8.1", NutResponseType.Ok)]
    [InlineData("1.2", NutResponseType.Ok)]
    [InlineData("something-unexpected", NutResponseType.Unrecognized)]
    public void ParseResponseType_MatchesOriginalSwitch(string line, NutResponseType expected)
    {
        var split = line.Split(' ', 4);
        var actual = NutSocket.ParseResponseType(split);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("VAR-NOT-SUPPORTED", NutResponseType.VarNotSupported)]
    [InlineData("DATA-STALE", NutResponseType.DataStale)]
    [InlineData("UNKNOWN-UPS", NutResponseType.UnknownUps)]
    [InlineData("ACCESS-DENIED", NutResponseType.AccessDenied)]
    [InlineData("bogus-code", NutResponseType.Unrecognized)]
    public void ParseErrorCode_StripsDashesAndParsesEnum(string code, NutResponseType expected)
    {
        Assert.Equal(expected, NutSocket.ParseErrorCode(code));
    }
}
