using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Allo.Api.Migrations
{
    /// <inheritdoc />
    public partial class StoreColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "Stores",
                type: "TEXT",
                maxLength: 7,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Color",
                table: "Stores");
        }
    }
}
