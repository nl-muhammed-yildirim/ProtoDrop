using wa.workers;
using Xunit;

namespace wa.workers.unit;

public class ZipFunctionTests
{
    [Fact]
    public void DeduplicateName_TwoIdenticalFiles_ReturnsUniqueNames()
    {
        var usedNames = new HashSet<string>(StringComparer.Ordinal);

        var name1 = ZipFunction.DeduplicateName("report.pdf", usedNames);
        var name2 = ZipFunction.DeduplicateName("report.pdf", usedNames);

        Assert.Equal("report.pdf", name1);
        Assert.Equal("report_1.pdf", name2);
    }

    [Fact]
    public void DeduplicateName_ThreeIdenticalFiles_ReturnsUniqueNames()
    {
        var usedNames = new HashSet<string>(StringComparer.Ordinal);

        var name1 = ZipFunction.DeduplicateName("report.pdf", usedNames);
        var name2 = ZipFunction.DeduplicateName("report.pdf", usedNames);
        var name3 = ZipFunction.DeduplicateName("report.pdf", usedNames);

        Assert.Equal("report.pdf", name1);
        Assert.Equal("report_1.pdf", name2);
        Assert.Equal("report_2.pdf", name3);
    }

    [Fact]
    public void DeduplicateName_FilesWithoutExtension_ReturnsUniqueNames()
    {
        var usedNames = new HashSet<string>(StringComparer.Ordinal);

        var name1 = ZipFunction.DeduplicateName("README", usedNames);
        var name2 = ZipFunction.DeduplicateName("README", usedNames);

        Assert.Equal("README", name1);
        Assert.Equal("README_1", name2);
    }

    [Fact]
    public void DeduplicateName_DifferentFiles_ReturnsOriginalNames()
    {
        var usedNames = new HashSet<string>(StringComparer.Ordinal);

        var name1 = ZipFunction.DeduplicateName("file1.txt", usedNames);
        var name2 = ZipFunction.DeduplicateName("file2.txt", usedNames);

        Assert.Equal("file1.txt", name1);
        Assert.Equal("file2.txt", name2);
    }

    [Fact]
    public void DeduplicateName_MixedDuplicates_ReturnsCorrectNames()
    {
        var usedNames = new HashSet<string>(StringComparer.Ordinal);

        var name1 = ZipFunction.DeduplicateName("report.pdf", usedNames);
        var name2 = ZipFunction.DeduplicateName("other.pdf", usedNames);
        var name3 = ZipFunction.DeduplicateName("report.pdf", usedNames);

        Assert.Equal("report.pdf", name1);
        Assert.Equal("other.pdf", name2);
        Assert.Equal("report_1.pdf", name3);
    }
}
