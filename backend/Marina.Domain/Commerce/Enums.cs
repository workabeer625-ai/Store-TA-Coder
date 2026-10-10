namespace Marina.Domain.Commerce;
public enum OrderStatus { Pending, Confirmed, Preparing, Ready, Shipped, OutForDelivery, Delivered, Cancelled, Returned, Refunded }
public enum StockMovementType { Restock, Sale, Cancellation, Return, ManualAdjustment, Damage, Correction }
public enum DiscountType { Percentage, FixedAmount }
public enum PaymentStatus { Pending, Authorized, Captured, Failed, Cancelled, Refunded }
public enum NotificationChannel { InApp, Email, Sms, Push }
