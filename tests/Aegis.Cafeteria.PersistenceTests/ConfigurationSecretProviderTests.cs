namespace Aegis.Cafeteria.PersistenceTests;
using Aegis.Cafeteria.Application.Abstractions; using Aegis.Cafeteria.Infrastructure.Secrets; using Microsoft.Extensions.Configuration;
public sealed class ConfigurationSecretProviderTests
{
 [Fact] public async Task ReadsDevelopmentMessagingSecret(){var c=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{$"DevelopmentSecrets:{ApplicationSecretNames.MessagingBearerToken}","abc"}}).Build();var p=new ConfigurationSecretProvider(c);Assert.Equal("abc",await p.GetSecretAsync(ApplicationSecretNames.MessagingBearerToken));}
}
