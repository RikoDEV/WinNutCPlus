namespace WinNUT.Core.Models;

/// <summary>
/// Encapsulates a single query/response exchange between a NUT client and server.
/// </summary>
public sealed class Transaction
{
    /// <summary>The original query sent by the client, to the server.</summary>
    public string Query { get; }

    /// <summary>See <see cref="NutResponseType"/>.</summary>
    public NutResponseType ResponseType { get; }

    /// <summary>The full, unaltered response line from the server.</summary>
    public string? RawResponse { get; }

    /// <summary>The <see cref="RawResponse"/> split around the delimiter character (space), max 4 tokens.</summary>
    public string[]? SplitResponse { get; }

    public Transaction(string query, string? response, NutResponseType responseType, string[]? splitResponse = null)
    {
        Query = query;
        RawResponse = response;
        ResponseType = responseType;
        SplitResponse = splitResponse;
    }
}

/// <summary>
/// Raised when the NUT server returns a defined protocol error, or an unrecognized response.
/// </summary>
public sealed class NutException : ApplicationException
{
    public Transaction LastTransaction { get; }

    public NutException(Transaction transaction)
        : base($"{transaction.ResponseType} ({transaction.RawResponse}){Environment.NewLine}Query: {transaction.Query}")
    {
        LastTransaction = transaction;
    }
}

/// <summary>
/// Connection parameters for a NUT server + target UPS.
/// </summary>
public sealed class NutParameter
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string UpsName { get; set; } = string.Empty;
    public bool AutoReconnect { get; set; }

    public NutParameter() { }

    public NutParameter(string host, int port, string login, string password, string upsName, bool autoReconnect = false)
    {
        Host = host;
        Port = port;
        Login = login;
        Password = password;
        UpsName = upsName;
        AutoReconnect = autoReconnect;
    }

    /// <summary>Informative representation. Note: password is intentionally not printed.</summary>
    public override string ToString()
    {
        var suffix = AutoReconnect ? " [AutoReconnect]" : string.Empty;
        return $"{Login}@{Host}:{Port}, Name: {UpsName}{suffix}";
    }
}
