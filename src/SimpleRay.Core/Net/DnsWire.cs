namespace SimpleRay.Core.Net;

/// <summary>
/// Minimal DNS wire-format helpers shared by the DoH and UDP probes so both judge a
/// resolver by the same rule: a real, successful answer — not merely a reachable port.
/// </summary>
internal static class DnsWire
{
    /// <summary>A minimal query for example.com/A with recursion desired and id 0.</summary>
    public static byte[] BuildQuery() => new byte[]
    {
        0x00, 0x00,             // id 0 (fixed so a DoH GET stays cacheable)
        0x01, 0x00,             // flags: standard query, recursion desired
        0x00, 0x01,             // 1 question
        0x00, 0x00,             // 0 answers
        0x00, 0x00,             // 0 authority
        0x00, 0x00,             // 0 additional
        7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e',
        3, (byte)'c', (byte)'o', (byte)'m',
        0x00,                   // end of name
        0x00, 0x01,             // QTYPE A
        0x00, 0x01,             // QCLASS IN
    };

    /// <summary>True when the bytes are a DNS reply with RCODE 0 and at least one answer.</summary>
    public static bool IsSuccessfulResponse(byte[] body)
    {
        if (body.Length < 12) return false;
        bool isResponse = (body[2] & 0x80) != 0;
        int rcode = body[3] & 0x0F;
        int answerCount = (body[6] << 8) | body[7];
        return isResponse && rcode == 0 && answerCount > 0;
    }
}
