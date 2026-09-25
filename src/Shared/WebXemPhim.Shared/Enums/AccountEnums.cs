namespace WebXemPhim.Shared.Enums;

public enum MembershipTier
{
    Free = 1,
    Vip = 2,
    Premium = 3
}

public enum BillingCycle { Monthly = 1, Yearly = 2 }

public enum PaymentMethod
{
    BankTransfer = 1,
    Momo = 2,
    Visa = 3
}

public enum PaymentStatus
{
    Pending = 1,
    Completed = 2,
    Failed = 3
}
