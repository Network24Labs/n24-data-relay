using Microsoft.Extensions.Configuration;
using N24DataRelay.Core.Constants;
using N24DataRelay.Core.Models;

namespace N24DataRelay.Core.Tests;

public class ConfigurationBindingTests
{
    [Fact]
    public void N24DataRelaySection_Binds_To_N24DataRelayConfiguration()
    {
        var json = """
            {
                "N24DataRelay": {
                    "Service": {
                        "Enabled": true,
                        "WatchDirectory": "/tmp/watch",
                        "TransferMethod": "ssh"
                    },
                    "Transfer": {
                        "Ssh": {
                            "Host": "host.example.com",
                            "Port": 22,
                            "Username": "relay"
                        }
                    }
                }
            }
            """;
        var config = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            .Build();
        var section = config.GetSection(ApplicationConstants.Configuration.SectionName);
        var options = new N24DataRelayConfiguration();
        section.Bind(options);

        Assert.True(options.Service.Enabled);
        Assert.Equal("/tmp/watch", options.Service.WatchDirectory);
        Assert.Equal("ssh", options.Service.TransferMethod);
        Assert.Equal("host.example.com", options.Transfer.Ssh.Host);
        Assert.Equal(22, options.Transfer.Ssh.Port);
        Assert.Equal("relay", options.Transfer.Ssh.Username);
    }
}
