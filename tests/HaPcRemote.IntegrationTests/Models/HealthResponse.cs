namespace HaPcRemote.IntegrationTests.Models;

public class HealthResponse
{
    public string Status { get; set; } = string.Empty;
    public string? MachineName { get; set; }
    public string? Version { get; set; }
    public List<MacAddressInfo>? MacAddresses { get; set; }
}

public class MacAddressInfo
{
    public string InterfaceName { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
}
