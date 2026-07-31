using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hold.Data.Migrations
{
    /// <inheritdoc />
    public partial class Accounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Settings",
                table: "Settings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Settings_SingleRow",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "Settings");

            migrationBuilder.AddColumn<string>(
                name: "ShareToken",
                table: "WishLists",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Settings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Not scaffolded, and the reason EF warned about data loss. The new column would
            // otherwise leave the existing row owned by the empty string, where the adoption in
            // GoogleSignIn — which looks for "me" — would never find it, and the wait and
            // currency chosen before accounts existed would be silently abandoned.
            //
            // Safe as a blanket UPDATE because the CK_Settings_SingleRow constraint dropped
            // just above guaranteed there was never more than one row to begin with.
            migrationBuilder.Sql($"""UPDATE "Settings" SET "OwnerId" = '{WishList.UnclaimedOwnerId}';""");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Settings",
                table: "Settings",
                column: "OwnerId");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GoogleSubject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WishLists_ShareToken",
                table: "WishLists",
                column: "ShareToken",
                unique: true,
                filter: "\"ShareToken\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_GoogleSubject",
                table: "Users",
                column: "GoogleSubject",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_WishLists_ShareToken",
                table: "WishLists");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Settings",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ShareToken",
                table: "WishLists");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Settings");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "Settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Also not scaffolded. Going back means going back to one row keyed to 1, and the
            // check constraint at the end of this method would reject the 0 the column default
            // leaves behind. Rows beyond the first cannot be kept — the old schema had no way
            // to represent more than one, and OwnerId no longer exists to choose between them,
            // so the surviving row is picked by physical position.
            migrationBuilder.Sql("""
                DELETE FROM "Settings" WHERE ctid NOT IN (SELECT MIN(ctid) FROM "Settings");
                UPDATE "Settings" SET "Id" = 1;
                """);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Settings",
                table: "Settings",
                column: "Id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Settings_SingleRow",
                table: "Settings",
                sql: "\"Id\" = 1");
        }
    }
}
