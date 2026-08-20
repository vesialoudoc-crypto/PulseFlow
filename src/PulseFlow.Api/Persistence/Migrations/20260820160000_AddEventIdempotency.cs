using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PulseFlow.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "event_id",
                table: "events",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("UPDATE events SET event_id = id WHERE event_id IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "event_id",
                table: "events",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_events_source_event_id",
                table: "events",
                columns: new[] { "source", "event_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_events_source_event_id",
                table: "events");

            migrationBuilder.DropColumn(
                name: "event_id",
                table: "events");
        }
    }
}
