using System.IO;
using Xunit;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;

namespace AutomationPortal.App.Tests;

public class TemplateCatalogTests
{
    private static string Gallery()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "gallery")) && File.Exists(Path.Combine(dir.FullName, "AutomationPortal.sln"))) return Path.Combine(dir.FullName, "gallery");
        throw new InvalidOperationException("gallery folder not found");
    }

    private static Shortcut Load(TemplateInfo t) => ShortcutPackage.Parse(File.ReadAllText(Path.Combine(Gallery(), t.File)));

    [Fact]
    public void Sentence_splits_into_words_blanks_and_when()
    {
        var parts = TemplateCatalog.Parse("Tidy {0} into folders by {1}, {when}. {x}");
        Assert.Equal(new TemplatePart[]
        {
            new TemplateWords("Tidy "), new TemplateBlank(0), new TemplateWords(" into folders by "), new TemplateBlank(1),
            new TemplateWords(", "), new TemplateWhen(), new TemplateWords(". {x}"),
        }, parts);
    }

    [Fact]
    public void Every_template_is_in_the_gallery_and_its_blanks_are_questions_of_the_shortcut()
    {
        var live = TemplateCatalog.All.Where(t => !t.ComingSoon).ToList();
        Assert.Equal(4, live.Count);
        Assert.Single(TemplateCatalog.All, t => t.ComingSoon);
        foreach (var t in live)
        {
            var s = Load(t);
            Assert.Same(t, TemplateCatalog.ForFile(Path.Combine("x", t.File)));
            var blanks = TemplateCatalog.Parse(t.Sentence).OfType<TemplateBlank>().Select(b => b.Question).ToList();
            Assert.Equal(Enumerable.Range(0, s.ImportQuestions.Count), blanks.OrderBy(i => i));
            Assert.Equal(t.Schedule is not null, TemplateCatalog.Parse(t.Sentence).OfType<TemplateWhen>().Any());
        }
        Assert.Null(TemplateCatalog.ForFile("organize-downloads.autoportal"));
    }

    [Fact]
    public void Daily_weekday_and_weekly_schedules_become_time_of_day_automations()
    {
        var id = Guid.NewGuid();
        Assert.Null(new TemplateSchedule(TemplateRepeat.Off, new TimeOnly(8, 0)).ToAutomation(id, false));

        var daily = new TemplateSchedule(TemplateRepeat.Daily, new TimeOnly(8, 0)).ToAutomation(id, runIfMissed: true)!;
        Assert.Equal("time.daily", daily.Trigger.TriggerId);
        Assert.Equal(id, daily.ShortcutId);
        Assert.True(daily.Enabled && daily.RunIfMissed);
        Assert.Equal(new TimeOnly(8, 0), ((TimeValue)daily.Trigger.Parameters["time"]).Value);
        Assert.Equal(7, ((ListValue)daily.Trigger.Parameters["days"]).Items.Count);

        var weekdays = new TemplateSchedule(TemplateRepeat.Weekdays, new TimeOnly(9, 30)).ToAutomation(id, false)!;
        Assert.Equal(new[] { "mon", "tue", "wed", "thu", "fri" }, ((ListValue)weekdays.Trigger.Parameters["days"]).Items.Select(i => i.ToLiteralText()));
        Assert.Equal("every day at 08:00", new TemplateSchedule(TemplateRepeat.Daily, new TimeOnly(8, 0)).Describe());
        Assert.Equal(new TimeOnly(7, 5), TemplateSchedule.ParseTime("7:05"));
        Assert.Null(TemplateSchedule.ParseTime("7 o'clock"));
    }

    [Fact]
    public void Fill_writes_answers_and_extras_into_a_copy_and_backup_needs_a_place_first()
    {
        var tidy = Load(TemplateCatalog.All[0]);
        var filled = TemplateCatalog.Fill(tidy, new ParamValue?[] { ParamValue.Choice("chosen"), ParamValue.Text("{year}"), null },
            new[] { ("tidyFolder", "chosen", (ParamValue)ParamValue.Text(@"C:\Temp\x")) });
        Assert.Equal("chosen", ((ChoiceValue)filled.FindAction("tidyFolder")!.Parameters["folder"]).Value);
        Assert.Equal(@"C:\Temp\x", filled.FindAction("tidyFolder")!.Parameters["chosen"].ToLiteralText());
        Assert.Equal("{year}", filled.FindAction("tidyMove01")!.Parameters["subfolder"].ToLiteralText());
        Assert.Equal(7, ((NumberValue)filled.FindAction("tidyOld001")!.Parameters["olderThanDays"]).Value);
        Assert.Equal("downloads", ((ChoiceValue)tidy.FindAction("tidyFolder")!.Parameters["folder"]).Value);   // the template itself is untouched
        Assert.Null(TemplateCatalog.Missing(tidy, new ParamValue?[3]));

        var backup = Load(TemplateCatalog.All.Single(t => t.File == "template-folder-backup.autoportal"));
        Assert.NotNull(TemplateCatalog.Missing(backup, new ParamValue?[2]));
        Assert.Null(TemplateCatalog.Missing(backup, new ParamValue?[] { null, ParamValue.Text(@"C:\Temp\Backups") }));
    }
}
