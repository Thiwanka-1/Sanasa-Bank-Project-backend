// =============================================
// File: AccountEnums.cs
// Description: Enums used by AccountType & Account
// =============================================
namespace EvCharge.Api.Domain
{
    public enum AccountCategory
    {
        MemberDeposits = 1,
        NonMemberDeposits = 2
    }

    // How interest/dividends should be computed for this type
    public enum InterestMethod
    {
        None = 0,
        Quarterly_MinBalance = 1, // savings-like: 3-month min balance, credit at quarter end
        FD_Maturity = 2,          // fixed deposit: pay at maturity (tenor-based)
        Dividend_Like = 3         // shares: dividend-like logic (no interest)
    }
}
