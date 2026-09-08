namespace Aegis.Cafeteria.UnitTests.Customers;
using Aegis.Cafeteria.Domain.Customers;
public sealed class CustomerTests
{
 [Fact] public void Constructor_StoresCurrentCafeteriaIdentityOnly(){var photo=new byte[]{1,2};var c=new Customer(10," Test Customer ",photo,true,5000m);Assert.Equal(10,c.Id);Assert.Equal("Test Customer",c.Name);Assert.Same(photo,c.Photo);Assert.True(c.IsEligibleForCredit);Assert.Equal(5000m,c.AdditionalCreditLimit);}
 [Theory][InlineData("")][InlineData(" ")] public void Constructor_BlankName_Throws(string name)=>Assert.Throws<ArgumentException>(()=>new Customer(10,name,null,true,0m));
 [Fact] public void Type_HasNoLegacyTicketOrDailyRateProperties(){var names=typeof(Customer).GetProperties().Select(x=>x.Name).ToHashSet();Assert.DoesNotContain("TicketsAssigned",names);Assert.DoesNotContain("TicketsRemaining",names);Assert.DoesNotContain("MonthlyCreditPerWorkingDay",names);Assert.DoesNotContain("CompanyContributionLimit",names);}
}
