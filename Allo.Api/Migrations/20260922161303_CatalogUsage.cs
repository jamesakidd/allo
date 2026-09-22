using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Allo.Api.Migrations
{
    /// <inheritdoc />
    public partial class CatalogUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PendingUnit",
                table: "Items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UseCount",
                table: "Items",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingUnit",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "UseCount",
                table: "Items");
        }
    }
}
