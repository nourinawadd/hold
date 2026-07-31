using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hold.Data.Migrations
{
    /// <inheritdoc />
    public partial class PriceRecheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LatestPrice",
                table: "Items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LatestPriceAt",
                table: "Items",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LatestPrice",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "LatestPriceAt",
                table: "Items");
        }
    }
}
