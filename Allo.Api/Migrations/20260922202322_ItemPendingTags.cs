using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Allo.Api.Migrations
{
    /// <inheritdoc />
    public partial class ItemPendingTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PendingTags",
                table: "Items",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingTags",
                table: "Items");
        }
    }
}
