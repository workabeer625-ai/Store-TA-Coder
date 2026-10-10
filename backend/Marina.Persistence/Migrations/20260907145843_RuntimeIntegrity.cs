using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marina.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RuntimeIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SizeTranslations_SizeOptionId",
                table: "SizeTranslations");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_ProductId_ColorId_SizeId",
                table: "ProductVariants");

            migrationBuilder.DropIndex(
                name: "IX_ColorTranslations_ColorOptionId",
                table: "ColorTranslations");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "SizeTranslations",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Culture",
                table: "SizeTranslations",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Sizes",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ColorTranslations",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Culture",
                table: "ColorTranslations",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Hex",
                table: "Colors",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Colors",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Brands",
                type: "nvarchar(180)",
                maxLength: 180,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_SizeTranslations_SizeOptionId_Culture",
                table: "SizeTranslations",
                columns: new[] { "SizeOptionId", "Culture" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sizes_Code",
                table: "Sizes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId_ColorId_SizeId",
                table: "ProductVariants",
                columns: new[] { "ProductId", "ColorId", "SizeId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Variant_Prices",
                table: "ProductVariants",
                sql: "[Price] >= 0 AND ([CompareAtPrice] IS NULL OR [CompareAtPrice] >= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payment_Amount",
                table: "Payments",
                sql: "[Amount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Order_Amounts",
                table: "Orders",
                sql: "[Subtotal] >= 0 AND [Discount] >= 0 AND [Shipping] >= 0 AND [Tax] >= 0 AND [Total] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderItem_Values",
                table: "OrderItems",
                sql: "[Quantity] >= 1 AND [Quantity] <= 20 AND [UnitPrice] >= 0 AND [Discount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Coupon_Values",
                table: "Coupons",
                sql: "[Type] IN (0,1) AND [Value] >= 0 AND ([Type] <> 0 OR [Value] <= 100) AND ([MinimumPurchase] IS NULL OR [MinimumPurchase] >= 0) AND ([MaximumDiscount] IS NULL OR [MaximumDiscount] >= 0) AND [ValidToUtc] >= [ValidFromUtc] AND ([MaxUses] IS NULL OR [MaxUses] > 0) AND ([MaxUsesPerCustomer] IS NULL OR [MaxUsesPerCustomer] > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_ColorTranslations_ColorOptionId_Culture",
                table: "ColorTranslations",
                columns: new[] { "ColorOptionId", "Culture" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Colors_Code",
                table: "Colors",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Brands_Slug",
                table: "Brands",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Addresses_UserId",
                table: "Addresses",
                column: "UserId",
                unique: true,
                filter: "[IsDefault] = 1 AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SizeTranslations_SizeOptionId_Culture",
                table: "SizeTranslations");

            migrationBuilder.DropIndex(
                name: "IX_Sizes_Code",
                table: "Sizes");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_ProductId_ColorId_SizeId",
                table: "ProductVariants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Variant_Prices",
                table: "ProductVariants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payment_Amount",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Order_Amounts",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderItem_Values",
                table: "OrderItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Coupon_Values",
                table: "Coupons");

            migrationBuilder.DropIndex(
                name: "IX_ColorTranslations_ColorOptionId_Culture",
                table: "ColorTranslations");

            migrationBuilder.DropIndex(
                name: "IX_Colors_Code",
                table: "Colors");

            migrationBuilder.DropIndex(
                name: "IX_Brands_Slug",
                table: "Brands");

            migrationBuilder.DropIndex(
                name: "IX_Addresses_UserId",
                table: "Addresses");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "SizeTranslations",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(160)",
                oldMaxLength: 160);

            migrationBuilder.AlterColumn<string>(
                name: "Culture",
                table: "SizeTranslations",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Sizes",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ColorTranslations",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(160)",
                oldMaxLength: 160);

            migrationBuilder.AlterColumn<string>(
                name: "Culture",
                table: "ColorTranslations",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "Hex",
                table: "Colors",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(9)",
                oldMaxLength: 9);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Colors",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Brands",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(180)",
                oldMaxLength: 180);

            migrationBuilder.CreateIndex(
                name: "IX_SizeTranslations_SizeOptionId",
                table: "SizeTranslations",
                column: "SizeOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId_ColorId_SizeId",
                table: "ProductVariants",
                columns: new[] { "ProductId", "ColorId", "SizeId" },
                unique: true,
                filter: "[ColorId] IS NOT NULL AND [SizeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ColorTranslations_ColorOptionId",
                table: "ColorTranslations",
                column: "ColorOptionId");
        }
    }
}
