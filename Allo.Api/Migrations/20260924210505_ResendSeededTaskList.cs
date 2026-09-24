using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Allo.Api.Migrations
{
    // AddTasks seeded the default "Tasks" list with HasData, which stamps the seed sequence
    // (1). On a live install every phone had already synced past 1, so a pull for
    // "everything after N" never included the list: its tasks arrived, the list did not, and
    // the picker showed the list's id and could not rename it.
    //
    // Re-stamping the row with the next counter value makes it new to every phone, so each
    // picks it up on its next sync. The number is taken from the counter, not invented, so no
    // later change can be handed the same one. On a fresh install this is harmless: phones
    // there pull from zero and would have received the list either way.
    //
    // Lesson for future releases: never seed a synced row in a migration that ships after
    // installs exist. HasData cannot know the live counter.
    /// <inheritdoc />
    public partial class ResendSeededTaskList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE SyncCounter SET Value = Value + 1 WHERE Id = 1;");
            // SQLite stores Guids as text; compare case-insensitively rather than trusting
            // the casing EF happened to write.
            migrationBuilder.Sql("""
                UPDATE TaskLists
                SET Sequence = (SELECT Value FROM SyncCounter WHERE Id = 1)
                WHERE upper(Id) = '00000000-0000-0000-0000-0000000022A1';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: a row that has been sent to phones cannot be un-sent, and an
            // older sequence number would only hide it again.
        }
    }
}
