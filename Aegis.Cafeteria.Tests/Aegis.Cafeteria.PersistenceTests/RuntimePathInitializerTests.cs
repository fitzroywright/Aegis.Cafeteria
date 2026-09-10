namespace Aegis.Cafeteria.PersistenceTests;
using Aegis.Cafeteria.Infrastructure.Configuration;
public sealed class RuntimePathInitializerTests
{
 [Fact] public void EnsureDirectories_CreatesLocalDatabaseLogAndHealthParents(){var root=Path.Combine(Path.GetTempPath(),"aegis-paths-"+Guid.NewGuid());try{var runtime=new CafeteriaRuntimeOptions(new CafeteriaPersistenceOptions{LocalSqliteConnectionString=$"Data Source={Path.Combine(root,"db","cafeteria.db")}"},new CafeteriaLoggingOptions{LocalLogPath=Path.Combine(root,"logs","a-.log")},new BitwardenSecretsManagerOptions(),new FfpIntegrationOptions{Enabled=false},new CafeteriaHealthOptions{HeartbeatPath=Path.Combine(root,"health","heartbeat.json")},new CafeteriaMessagingOptions(),new CafeteriaServicesOptions());RuntimePathInitializer.EnsureDirectories(runtime);Assert.True(Directory.Exists(Path.Combine(root,"db")));Assert.True(Directory.Exists(Path.Combine(root,"logs")));Assert.True(Directory.Exists(Path.Combine(root,"health")));}finally{if(Directory.Exists(root))Directory.Delete(root,true);}}
}
