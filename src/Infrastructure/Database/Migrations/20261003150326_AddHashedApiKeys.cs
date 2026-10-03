using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <summary>
    /// Replaces the readable sensor key with its SHA-256 fingerprint plus a 4-character hint.
    /// Existing keys are converted in place BEFORE the readable column is dropped, so the sensor already
    /// running keeps working with the same key. The scaffolded order (drop first) would have destroyed it.
    /// </summary>
    /// <inheritdoc />
    public partial class AddHashedApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeyHash",
                schema: "public",
                table: "ApiKey",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "KeyHint",
                schema: "public",
                table: "ApiKey",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            // sha256() of the UTF-8 text equals SHA256.HashData(Encoding.UTF8.GetBytes(key)) as lowercase hex,
            // which is what ApiKeyCrypto.Hash computes (covered by a unit test with a known vector).
            migrationBuilder.Sql(@"
                UPDATE ""ApiKey""
                SET ""KeyHash"" = encode(sha256(convert_to(""Key"", 'UTF8')), 'hex'),
                    ""KeyHint"" = right(""Key"", 4);");

            // The temporary defaults were only there to add NOT NULL columns to existing rows.
            migrationBuilder.Sql(@"ALTER TABLE ""ApiKey"" ALTER COLUMN ""KeyHash"" DROP DEFAULT;");
            migrationBuilder.Sql(@"ALTER TABLE ""ApiKey"" ALTER COLUMN ""KeyHint"" DROP DEFAULT;");

            migrationBuilder.DropColumn(
                name: "Key",
                schema: "public",
                table: "ApiKey");

            migrationBuilder.CreateIndex(
                name: "IX_Location_SerialNumber",
                schema: "public",
                table: "Location",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKey_KeyHash",
                schema: "public",
                table: "ApiKey",
                column: "KeyHash",
                unique: true);
        }

        /// <summary>
        /// Plain-text keys cannot be restored. The column is re-created and filled with the fingerprint so the
        /// schema round-trips; sensors using their original keys will be rejected until keys are rotated.
        /// </summary>
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Location_SerialNumber",
                schema: "public",
                table: "Location");

            migrationBuilder.DropIndex(
                name: "IX_ApiKey_KeyHash",
                schema: "public",
                table: "ApiKey");

            migrationBuilder.AddColumn<string>(
                name: "Key",
                schema: "public",
                table: "ApiKey",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"UPDATE ""ApiKey"" SET ""Key"" = ""KeyHash"";");

            migrationBuilder.DropColumn(
                name: "KeyHash",
                schema: "public",
                table: "ApiKey");

            migrationBuilder.DropColumn(
                name: "KeyHint",
                schema: "public",
                table: "ApiKey");
        }
    }
}
