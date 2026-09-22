using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Allo.Api.Migrations
{
    /// <inheritdoc />
    public partial class DataModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncCounter",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncCounter", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_Categories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Categories_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShoppingLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShoppingLists_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stores_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DefaultCategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DefaultUnit = table.Column<string>(type: "TEXT", nullable: false),
                    Aliases = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultTags = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Items_Categories_DefaultCategoryId",
                        column: x => x.DefaultCategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Items_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoreCategoryOrders",
                columns: table => new
                {
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreCategoryOrders", x => new { x.StoreId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_StoreCategoryOrders_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreCategoryOrders_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreCategoryOrders_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ListEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ListId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    AddedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsChecked = table.Column<bool>(type: "INTEGER", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CheckedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListEntries_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ListEntries_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ListEntries_ShoppingLists_ListId",
                        column: x => x.ListId,
                        principalTable: "ShoppingLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ListEntries_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ListEntries_Users_AddedBy",
                        column: x => x.AddedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ListEntries_Users_CheckedBy",
                        column: x => x.CheckedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ListEntries_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "IsDeleted", "Name", "ParentId", "Sequence", "SortOrder", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-00000000c001"), false, "Produce", null, 1L, 10, new DateTimeOffset(new DateTime(2026, 9, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-0000-0000-00000000c002"), false, "Bakery", null, 1L, 20, new DateTimeOffset(new DateTime(2026, 9, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-0000-0000-00000000c003"), false, "Dairy", null, 1L, 30, new DateTimeOffset(new DateTime(2026, 9, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-0000-0000-00000000c004"), false, "Meat & Seafood", null, 1L, 40, new DateTimeOffset(new DateTime(2026, 9, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-0000-0000-00000000c005"), false, "Frozen", null, 1L, 50, new DateTimeOffset(new DateTime(2026, 9, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-0000-0000-00000000c006"), false, "Pantry", null, 1L, 60, new DateTimeOffset(new DateTime(2026, 9, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-0000-0000-00000000c007"), false, "Beverages", null, 1L, 70, new DateTimeOffset(new DateTime(2026, 9, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-0000-0000-00000000c008"), false, "Snacks", null, 1L, 80, new DateTimeOffset(new DateTime(2026, 9, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-0000-0000-00000000c009"), false, "Household", null, 1L, 90, new DateTimeOffset(new DateTime(2026, 9, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-0000-0000-00000000c00a"), false, "Personal Care", null, 1L, 100, new DateTimeOffset(new DateTime(2026, 9, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-0000-0000-00000000c00b"), false, "Baby", null, 1L, 110, new DateTimeOffset(new DateTime(2026, 9, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-0000-0000-00000000c00c"), false, "Pet", null, 1L, 120, new DateTimeOffset(new DateTime(2026, 9, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-0000-0000-00000000c0ff"), false, "Uncategorized", null, 1L, 2147483647, new DateTimeOffset(new DateTime(2026, 9, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                });

            migrationBuilder.InsertData(
                table: "ShoppingLists",
                columns: new[] { "Id", "IsDeleted", "Name", "Sequence", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new Guid("00000000-0000-0000-0000-0000000011a1"), false, "Groceries", 1L, new DateTimeOffset(new DateTime(2026, 9, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null });

            migrationBuilder.InsertData(
                table: "SyncCounter",
                columns: new[] { "Id", "Value" },
                values: new object[] { 1, 1L });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentId",
                table: "Categories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Sequence",
                table: "Categories",
                column: "Sequence");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_UpdatedBy",
                table: "Categories",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Items_DefaultCategoryId",
                table: "Items",
                column: "DefaultCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_NormalizedName",
                table: "Items",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_Items_Sequence",
                table: "Items",
                column: "Sequence");

            migrationBuilder.CreateIndex(
                name: "IX_Items_UpdatedBy",
                table: "Items",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ListEntries_AddedBy",
                table: "ListEntries",
                column: "AddedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ListEntries_CategoryId",
                table: "ListEntries",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ListEntries_CheckedBy",
                table: "ListEntries",
                column: "CheckedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ListEntries_ItemId",
                table: "ListEntries",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ListEntries_ListId",
                table: "ListEntries",
                column: "ListId");

            migrationBuilder.CreateIndex(
                name: "IX_ListEntries_Sequence",
                table: "ListEntries",
                column: "Sequence");

            migrationBuilder.CreateIndex(
                name: "IX_ListEntries_StoreId",
                table: "ListEntries",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ListEntries_UpdatedBy",
                table: "ListEntries",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingLists_Sequence",
                table: "ShoppingLists",
                column: "Sequence");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingLists_UpdatedBy",
                table: "ShoppingLists",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_StoreCategoryOrders_CategoryId",
                table: "StoreCategoryOrders",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreCategoryOrders_Sequence",
                table: "StoreCategoryOrders",
                column: "Sequence");

            migrationBuilder.CreateIndex(
                name: "IX_StoreCategoryOrders_UpdatedBy",
                table: "StoreCategoryOrders",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Stores_Sequence",
                table: "Stores",
                column: "Sequence");

            migrationBuilder.CreateIndex(
                name: "IX_Stores_UpdatedBy",
                table: "Stores",
                column: "UpdatedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListEntries");

            migrationBuilder.DropTable(
                name: "StoreCategoryOrders");

            migrationBuilder.DropTable(
                name: "SyncCounter");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "ShoppingLists");

            migrationBuilder.DropTable(
                name: "Stores");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
