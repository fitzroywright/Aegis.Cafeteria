namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

public sealed class EfCustomerAccountQuery(IDbContextFactory<LocalCafeteriaDbContext> factory,MonthlyCreditAllocationService allocationService,IExternalPublicationStatusProvider publicationStatusProvider):ICustomerAccountQuery
{
 public async Task<CustomerAccountView?> GetAsync(int customerId,int year,int month,CancellationToken cancellationToken=default)
 {
  if(customerId<=0)throw new ArgumentOutOfRangeException(nameof(customerId)); if(year<1)throw new ArgumentOutOfRangeException(nameof(year)); if(month is <1 or >12)throw new ArgumentOutOfRangeException(nameof(month)); CustomerRow? customerRow; AccountPeriodRow? periodRow;
  await using(LocalCafeteriaDbContext db=await factory.CreateDbContextAsync(cancellationToken)){customerRow=await db.Customers.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==customerId,cancellationToken);if(customerRow is null)return null;periodRow=await db.AccountPeriods.AsNoTracking().SingleOrDefaultAsync(x=>x.CustomerId==customerId&&x.Year==year&&x.Month==month,cancellationToken);}
  Customer customer=DomainReadMapper.ToDomain(customerRow); MonthlyCreditAllocation allocation=await allocationService.EnsureAsync(customer,year,month,cancellationToken); AccountPeriod period=new(year,month,allocation.EligibleWorkingDays); OrderRow[] orderRows; AccountEventRow[] events;
  await using(LocalCafeteriaDbContext db=await factory.CreateDbContextAsync(cancellationToken)){orderRows=await db.Orders.AsNoTracking().AsSplitQuery().Include(x=>x.Items).Include(x=>x.Payments).Include(x=>x.Refunds).ThenInclude(x=>x.Items).Include(x=>x.Refunds).ThenInclude(x=>x.Allocations).Where(x=>x.CustomerId==customerId&&x.AccountYear==year&&x.AccountMonth==month).ToArrayAsync(cancellationToken);events=(await db.AccountEvents.AsNoTracking().Where(x=>x.CustomerId==customerId&&x.AccountYear==year&&x.AccountMonth==month).ToArrayAsync(cancellationToken)).OrderBy(x=>x.OccurredAt).ThenBy(x=>x.Id).ToArray();}
  Order[] orders=orderRows.Select(DomainReadMapper.ToDomain).ToArray(); AdditionalCreditApproval[] approvals=Deserialize<AdditionalCreditApproval>(events,"AdditionalCreditApproval"); CustomerPayment[] payments=Deserialize<CustomerPayment>(events,"CustomerPayment"); CarriedForwardDebt[] carriedDebt=Deserialize<CarriedForwardDebt>(events,"CarriedForwardDebt"); PriorPeriodOutstandingSettlement[] settlements=Deserialize<PriorPeriodOutstandingSettlement>(events,"PriorPeriodOutstandingSettlement"); Stipend[] stipends=Deserialize<Stipend>(events,"Stipend"); PriorPeriodRefundCredit[] priorRefundCredits=Deserialize<PriorPeriodRefundCredit>(events,"PriorPeriodRefundCredit"); CustomerFinancialStatusView financial;
  if(!customer.IsEligibleForCredit)financial=NoCreditFinancialStatus();else{CustomerAccountSnapshot snapshot=new CustomerAccountCalculator().Calculate(customer,period,allocation,orders,approvals,payments,carriedDebt,settlements,stipends,priorRefundCredits);decimal payrollDeduction=Math.Max(0m,allocation.EmployeeFundedAllocation-snapshot.NonSubsidyAmountRemaining);decimal outstandingBalance=Math.Max(0m,snapshot.EmployeeAmountDue-payrollDeduction);financial=new(snapshot.CreditLimit,snapshot.SubsidyLimit,snapshot.CompanyContributionUsed,snapshot.SubsidyAmountRemaining,snapshot.NonSubsidyAmountRemaining,snapshot.TotalAvailableToSpend,snapshot.CarriedForwardDebt,snapshot.GrossCustomerLiability,snapshot.TotalOrderSales,snapshot.CafeteriaAccountTendered-snapshot.CafeteriaAccountRefunded,snapshot.CashTendered-snapshot.CashRefunded,snapshot.AdditionalCreditApproved,snapshot.AdditionalCreditUsed,snapshot.UnusedAdditionalCredit,snapshot.StipendCashPaid,snapshot.StipendCardReduction,snapshot.PriorPeriodOutstandingSettlements,snapshot.PriorPeriodRefundCredits,snapshot.CustomerPayments,snapshot.CustomerResponsibilityForCurrentCharges,outstandingBalance,snapshot.TotalRefunds,snapshot.CurrentCreditSaleDebt);}
  ExternalPublicationStatus publication=await publicationStatusProvider.GetAsync(cancellationToken); return new CustomerAccountView(new CustomerIdentityView(customer.Id,customer.Name,customer.Photo,customer.IsEligibleForCredit,false,customerRow.EmailAddress),new AccountPeriodView(year,month,allocation.EligibleWorkingDays,allocation.EmployeeFundedAllocation,allocation.SubsidyAllocation,periodRow?.IsClosed??false),financial,BuildTransactions(orderRows,events,year,month),publication,BuildFundingStatus(customer,allocation));
 }

 public async Task<CustomerAccountView?> GetSummaryAsync(int customerId,int year,int month,CancellationToken cancellationToken=default)
 {
  if(customerId<=0)throw new ArgumentOutOfRangeException(nameof(customerId)); if(year<1)throw new ArgumentOutOfRangeException(nameof(year)); if(month is <1 or >12)throw new ArgumentOutOfRangeException(nameof(month));

  CustomerRow? customerRow;
  AccountPeriodRow? periodRow;
  await using(LocalCafeteriaDbContext db=await factory.CreateDbContextAsync(cancellationToken))
  {
   customerRow=await db.Customers.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==customerId,cancellationToken);
   if(customerRow is null)return null;
   periodRow=await db.AccountPeriods.AsNoTracking().SingleOrDefaultAsync(x=>x.CustomerId==customerId&&x.Year==year&&x.Month==month,cancellationToken);
  }

  Customer customer=DomainReadMapper.ToDomain(customerRow);
  MonthlyCreditAllocation allocation=await allocationService.EnsureAsync(customer,year,month,cancellationToken);
  CustomerFinancialStatusView financial;

  if(!customer.IsEligibleForCredit)
  {
   financial=NoCreditFinancialStatus();
  }
  else
  {
   AccountEventRow[] events;
   Guid[] orderIds;
   decimal totalOrderSales=0m;
   decimal cafeteriaAccountTendered=0m;
   decimal cashTendered=0m;
   decimal creditTendered=0m;
   decimal totalRefunds=0m;
   decimal cafeteriaAccountRefunded=0m;
   decimal cashRefunded=0m;
   decimal creditRefunded=0m;

   await using(LocalCafeteriaDbContext db=await factory.CreateDbContextAsync(cancellationToken))
   {
    orderIds=await db.Orders.AsNoTracking()
      .Where(x=>x.CustomerId==customerId&&x.AccountYear==year&&x.AccountMonth==month&&x.Status==(int)OrderStatus.Completed)
      .Select(x=>x.Id)
      .ToArrayAsync(cancellationToken);

    events=await db.AccountEvents.AsNoTracking()
      .Where(x=>x.CustomerId==customerId&&x.AccountYear==year&&x.AccountMonth==month)
      .ToArrayAsync(cancellationToken);

    if(orderIds.Length>0)
    {
     OrderItemRow[] items=await db.OrderItems.AsNoTracking().Where(x=>orderIds.Contains(x.OrderId)).ToArrayAsync(cancellationToken);
     OrderPaymentRow[] payments=await db.OrderPayments.AsNoTracking().Where(x=>orderIds.Contains(x.OrderId)).ToArrayAsync(cancellationToken);
     OrderRefundRow[] refundRows=await db.OrderRefunds.AsNoTracking().Where(x=>orderIds.Contains(x.OrderId)).ToArrayAsync(cancellationToken);
     Guid[] refundIds=refundRows.Where(x=>x.RefundedAt.Year==year&&x.RefundedAt.Month==month).Select(x=>x.Id).ToArray();

     totalOrderSales=items.Sum(x=>x.UnitPrice*x.Quantity);
     cafeteriaAccountTendered=payments.Where(x=>x.Method==(int)PaymentMethod.CafeteriaAccount).Sum(x=>x.Amount);
     cashTendered=payments.Where(x=>x.Method==(int)PaymentMethod.Cash).Sum(x=>x.Amount);
     creditTendered=payments.Where(x=>x.Method==(int)PaymentMethod.Credit).Sum(x=>x.Amount);

     if(refundIds.Length>0)
     {
      OrderRefundItemRow[] refundItems=await db.OrderRefundItems.AsNoTracking().Where(x=>refundIds.Contains(x.RefundId)).ToArrayAsync(cancellationToken);
      RefundAllocationRow[] allocations=await db.RefundAllocations.AsNoTracking().Where(x=>refundIds.Contains(x.RefundId)).ToArrayAsync(cancellationToken);
      totalRefunds=refundItems.Sum(x=>x.UnitPrice*x.Quantity);
      cafeteriaAccountRefunded=allocations.Where(x=>x.Method==(int)PaymentMethod.CafeteriaAccount).Sum(x=>x.Amount);
      cashRefunded=allocations.Where(x=>x.Method==(int)PaymentMethod.Cash).Sum(x=>x.Amount);
      creditRefunded=allocations.Where(x=>x.Method==(int)PaymentMethod.Credit).Sum(x=>x.Amount);
     }
    }
   }

   AdditionalCreditApproval[] approvals=Deserialize<AdditionalCreditApproval>(events,"AdditionalCreditApproval");
   CustomerPayment[] customerPayments=Deserialize<CustomerPayment>(events,"CustomerPayment");
   CarriedForwardDebt[] carriedDebts=Deserialize<CarriedForwardDebt>(events,"CarriedForwardDebt");
   PriorPeriodOutstandingSettlement[] settlements=Deserialize<PriorPeriodOutstandingSettlement>(events,"PriorPeriodOutstandingSettlement");
   Stipend[] stipends=Deserialize<Stipend>(events,"Stipend");
   PriorPeriodRefundCredit[] priorRefundCreditEvents=Deserialize<PriorPeriodRefundCredit>(events,"PriorPeriodRefundCredit");

   decimal baseMonthlyCredit=allocation.TotalCreditLimit;
   decimal subsidyLimit=allocation.SubsidyAllocation;
   decimal carriedDebt=carriedDebts.Where(x=>x.CustomerId==customerId&&x.AppliedYear==year&&x.AppliedMonth==month).Sum(x=>x.Amount);
   decimal settlementAmount=settlements.Where(x=>x.CustomerId==customerId&&x.SettlementYear==year&&x.SettlementMonth==month).Sum(x=>x.Amount);
   decimal stipendCashPaid=stipends.Where(x=>x.CustomerId==customerId&&x.ProcessedAt.Year==year&&x.ProcessedAt.Month==month).Sum(x=>x.CashAmount);
   decimal stipendCardReduction=stipends.Where(x=>x.CustomerId==customerId&&x.ProcessedAt.Year==year&&x.ProcessedAt.Month==month).Sum(x=>x.CardReduction);
   if(stipendCardReduction>subsidyLimit)throw new InvalidOperationException("Stipend card reductions cannot exceed the period subsidy allocation.");

   decimal baseCreditAvailable=Math.Max(0m,baseMonthlyCredit-settlementAmount-stipendCardReduction-carriedDebt);
   decimal remainingCompanyContribution=Math.Max(0m,subsidyLimit-stipendCardReduction);
   decimal totalCharges=Math.Max(0m,cafeteriaAccountTendered-cafeteriaAccountRefunded);
   decimal currentCreditSaleDebt=Math.Max(0m,creditTendered-creditRefunded);
   decimal additionalCreditApproved=approvals.Where(x=>x.CustomerId==customerId&&x.ApprovedAt.Year==year&&x.ApprovedAt.Month==month).Sum(x=>x.Amount);
   if(additionalCreditApproved>customer.AdditionalCreditLimit)throw new InvalidOperationException("Approved additional credit exceeds the customer's additional credit limit.");
   decimal priorRefundCredits=priorRefundCreditEvents.Where(x=>x.CustomerId==customerId&&x.AppliedYear==year&&x.AppliedMonth==month).Sum(x=>x.CafeteriaAccountAmount);

   decimal subsidyEligibleCharges=Math.Min(totalCharges,baseCreditAvailable);
   decimal companyResponsibility=Math.Min(subsidyEligibleCharges,remainingCompanyContribution);
   decimal companyContributionUsed=stipendCardReduction+companyResponsibility;
   decimal subsidyRemaining=Math.Max(0m,subsidyLimit-companyContributionUsed);
   decimal customerResponsibility=Math.Max(0m,totalCharges-companyResponsibility);
   decimal grossLiability=carriedDebt+settlementAmount+customerResponsibility+currentCreditSaleDebt;
   decimal paid=customerPayments.Where(x=>x.CustomerId==customerId&&x.ReceivedAt.Year==year&&x.ReceivedAt.Month==month).Sum(x=>x.Amount);
   if(paid>grossLiability)throw new InvalidOperationException("Customer payments cannot exceed the customer's outstanding responsibility.");
   decimal employeeAmountDue=Math.Max(0m,grossLiability-paid);
   decimal paymentsAppliedToCreditDebt=Math.Min(paid,currentCreditSaleDebt);
   decimal paymentsAppliedToAllocationLiability=Math.Max(0m,paid-paymentsAppliedToCreditDebt);
   decimal totalSpendingCapacity=baseMonthlyCredit+additionalCreditApproved+priorRefundCredits;
   decimal totalAllocationUsage=carriedDebt+settlementAmount+stipendCardReduction+totalCharges;
   decimal netAllocationUsage=Math.Max(0m,totalAllocationUsage-paymentsAppliedToAllocationLiability);
   decimal availableCredit=Math.Clamp(totalSpendingCapacity-netAllocationUsage,0m,totalSpendingCapacity);
   decimal usageBeyondBase=Math.Max(0m,netAllocationUsage-baseMonthlyCredit);
   decimal additionalCreditUsed=Math.Min(additionalCreditApproved,usageBeyondBase);
   decimal unusedAdditionalCredit=Math.Max(0m,additionalCreditApproved-additionalCreditUsed);

   financial=new CustomerFinancialStatusView(
    baseMonthlyCredit,
    subsidyLimit,
    companyContributionUsed,
    subsidyRemaining,
    availableCredit,
    availableCredit,
    carriedDebt,
    grossLiability,
    totalOrderSales,
    cafeteriaAccountTendered-cafeteriaAccountRefunded,
    cashTendered-cashRefunded,
    additionalCreditApproved,
    additionalCreditUsed,
    unusedAdditionalCredit,
    stipendCashPaid,
    stipendCardReduction,
    settlementAmount,
    priorRefundCredits,
    paid,
    customerResponsibility,
    employeeAmountDue,
    totalRefunds,
    currentCreditSaleDebt);
  }

  return new CustomerAccountView(
   new CustomerIdentityView(customer.Id,customer.Name,customer.Photo,customer.IsEligibleForCredit,false,customerRow.EmailAddress),
   new AccountPeriodView(year,month,allocation.EligibleWorkingDays,allocation.EmployeeFundedAllocation,allocation.SubsidyAllocation,periodRow?.IsClosed??false),
   financial,
   Array.Empty<CustomerMonthlyTransactionView>(),
   new ExternalPublicationStatus(false,0,0,null,null),
   BuildFundingStatus(customer,allocation));
 }
 public async Task<IReadOnlyList<CustomerMonthlyTransactionView>> GetTransactionsAsync(int customerId,int year,int month,CancellationToken cancellationToken=default){if(customerId<=0)throw new ArgumentOutOfRangeException(nameof(customerId));await using LocalCafeteriaDbContext db=await factory.CreateDbContextAsync(cancellationToken);OrderRow[] orders=await db.Orders.AsNoTracking().AsSplitQuery().Include(x=>x.Items).Include(x=>x.Payments).Include(x=>x.Refunds).ThenInclude(x=>x.Items).Include(x=>x.Refunds).ThenInclude(x=>x.Allocations).Where(x=>x.CustomerId==customerId&&x.AccountYear==year&&x.AccountMonth==month).ToArrayAsync(cancellationToken);AccountEventRow[] events=(await db.AccountEvents.AsNoTracking().Where(x=>x.CustomerId==customerId&&x.AccountYear==year&&x.AccountMonth==month).ToArrayAsync(cancellationToken)).OrderBy(x=>x.OccurredAt).ThenBy(x=>x.Id).ToArray();return BuildTransactions(orders,events,year,month);}
 private static CustomerFinancialStatusView NoCreditFinancialStatus()=>new(0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0);
 private static CustomerFundingStatusView BuildFundingStatus(Customer customer,MonthlyCreditAllocation allocation){if(!customer.IsEligibleForCredit)return new(false,false,null,"Not eligible for cafeteria credit");bool policyLoaded=allocation.CreditLimitPrice>0m||allocation.SubsidyCost>0m;if(!policyLoaded)return new(false,false,null,"Cafeteria rate policy is not loaded; temporary zero funding is in effect.");return new(true,true,allocation.CalculatedAt,$"Monthly Cafeteria funding loaded: credit rate {allocation.CreditLimitPrice:N2}; subsidy rate {allocation.SubsidyCost:N2}");}
 private static T[] Deserialize<T>(IEnumerable<AccountEventRow> events,string type)=>events.Where(x=>x.EventType==type).Select(x=>PersistencePayloadSerializer.Deserialize<T>(x.PayloadJson)).ToArray();
 private static IReadOnlyList<CustomerMonthlyTransactionView> BuildTransactions(IEnumerable<OrderRow> orders,IEnumerable<AccountEventRow> events,int year,int month){List<CustomerMonthlyTransactionView> items=[];foreach(OrderRow order in orders.Where(x=>x.Status==(int)OrderStatus.Completed&&x.CompletedAt.HasValue)){string description=order.Items.Count==0?"Cafeteria purchase":string.Join(", ",order.Items.Select(x=>x.Quantity==1?x.ProductName:$"{x.Quantity} x {x.ProductName}"));decimal creditAmount=order.Payments.Where(x=>x.Method==(int)PaymentMethod.Credit).Sum(x=>x.Amount);if(creditAmount>0)description+=$" • Employee Credit {creditAmount:C2}";items.Add(new(order.CompletedAt!.Value,"Purchase",description,order.Items.Sum(x=>x.UnitPrice*x.Quantity),order.Payments.Where(x=>x.Method==(int)PaymentMethod.CafeteriaAccount).Sum(x=>x.Amount),order.Payments.Where(x=>x.Method==(int)PaymentMethod.Cash).Sum(x=>x.Amount),order.Id));foreach(OrderRefundRow refund in order.Refunds.Where(x=>x.RefundedAt.Year==year&&x.RefundedAt.Month==month)){decimal creditRefund=refund.Allocations.Where(x=>x.Method==(int)PaymentMethod.Credit).Sum(x=>x.Amount);items.Add(new(refund.RefundedAt,"Refund",creditRefund>0?$"{refund.Reason} • Employee Credit reduced {creditRefund:C2}":refund.Reason,-refund.Items.Sum(x=>x.UnitPrice*x.Quantity),-refund.Allocations.Where(x=>x.Method==(int)PaymentMethod.CafeteriaAccount).Sum(x=>x.Amount),-refund.Allocations.Where(x=>x.Method==(int)PaymentMethod.Cash).Sum(x=>x.Amount),refund.Id));}}foreach(AccountEventRow e in events){CustomerMonthlyTransactionView entry=e.EventType switch{"AdditionalCreditApproval"=>BuildAdditionalCredit(e),"CustomerPayment"=>BuildCustomerPayment(e),"CarriedForwardDebt"=>BuildCarriedDebt(e),"PriorPeriodOutstandingSettlement"=>BuildSettlement(e),"PriorPeriodRefundCredit"=>BuildPriorRefundCredit(e),"Stipend"=>BuildStipend(e),"ImportedFfpTransaction"=>BuildImportedFfpTransaction(e),_=>new(e.OccurredAt,e.EventType,e.EventType,e.Amount,0,0,e.Id)};items.Add(entry);}return items.OrderByDescending(x=>x.OccurredAt).ThenByDescending(x=>x.ReferenceId).ToArray();}
 private static CustomerMonthlyTransactionView BuildImportedFfpTransaction(AccountEventRow e){LegacyTransactionLedgerSnapshot x=JsonSerializer.Deserialize<LegacyTransactionLedgerSnapshot>(e.PayloadJson)??throw new InvalidOperationException("Imported FFP transaction payload is invalid.");if(x.ScanType==8){string name=string.IsNullOrWhiteSpace(x.IdName)?"Legacy cafeteria item":x.IdName.Trim();return new(x.OccurredAt,"Purchase",$"{name} (Imported FFP - ID Card)",x.ScanValue,x.ScanValue,0,e.Id);}if(x.ScanType==3)return new(x.OccurredAt,"Allocation",x.IdName,x.ScanValue,0,0,e.Id);if(x.ScanType==4)return new(x.OccurredAt,"Stipend",x.IdName,x.ScanValue,0,0,e.Id);return new(x.OccurredAt,"Imported FFP",x.IdName,x.ScanValue,0,0,e.Id);}
 private static CustomerMonthlyTransactionView BuildAdditionalCredit(AccountEventRow e){AdditionalCreditApproval x=PersistencePayloadSerializer.Deserialize<AdditionalCreditApproval>(e.PayloadJson);return new(e.OccurredAt,"Additional Credit",string.IsNullOrWhiteSpace(x.Reason)?"Manager-approved additional credit":x.Reason!,x.Amount,0,0,x.Id);}private static CustomerMonthlyTransactionView BuildCustomerPayment(AccountEventRow e){CustomerPayment x=PersistencePayloadSerializer.Deserialize<CustomerPayment>(e.PayloadJson);string detail=$"Customer payment - {x.PaymentMethod}"+(string.IsNullOrWhiteSpace(x.Reference)?string.Empty:$" - {x.Reference}")+$" - received by {x.ReceivedByUserId}";decimal before=x.OutstandingBalanceBefore??x.Amount;decimal after=x.OutstandingBalanceAfter??Math.Max(0,before-x.Amount);return new(e.OccurredAt,"Payment",detail,before,0,x.Amount,x.Id,before,after);}private static CustomerMonthlyTransactionView BuildCarriedDebt(AccountEventRow e){CarriedForwardDebt x=PersistencePayloadSerializer.Deserialize<CarriedForwardDebt>(e.PayloadJson);return new(e.OccurredAt,"Debt Brought Forward",$"Unpaid debt from {x.SourceYear:D4}-{x.SourceMonth:D2}",x.Amount,0,0,x.Id);}private static CustomerMonthlyTransactionView BuildSettlement(AccountEventRow e){PriorPeriodOutstandingSettlement x=PersistencePayloadSerializer.Deserialize<PriorPeriodOutstandingSettlement>(e.PayloadJson);return new(e.OccurredAt,"Prior Period Correction",x.Reason,x.Amount,0,0,x.Id);}private static CustomerMonthlyTransactionView BuildPriorRefundCredit(AccountEventRow e){PriorPeriodRefundCredit x=PersistencePayloadSerializer.Deserialize<PriorPeriodRefundCredit>(e.PayloadJson);return new(e.OccurredAt,"Prior Period Refund Credit",x.Reason,-x.CafeteriaAccountAmount,-x.CafeteriaAccountAmount,0,x.Id);}private static CustomerMonthlyTransactionView BuildStipend(AccountEventRow e){Stipend x=PersistencePayloadSerializer.Deserialize<Stipend>(e.PayloadJson);return new(e.OccurredAt,"Off-site Stipend",$"{x.PaymentMode} - cash {x.CashAmount:C}; card reduction {x.CardReduction:C}",x.CardReduction,x.CardReduction,0,x.Id);}
}
