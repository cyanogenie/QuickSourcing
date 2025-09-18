using System;
using System.Text.RegularExpressions;

class RegexTest
{
    static void Main()
    {
        var input = @"projectTitle: ""Sept bot test"",
Description: ""I want to source few xboxes for my team"",   
engagementStartDate: ""2025-10-16T18:30:00.000Z"",
engagementEndDate: ""2025-10-24T18:29:00.000Z"",
approxTotalBudget: 100,
email: ""sain@microsoft.com""";

        Console.WriteLine("Testing regex patterns on input:");
        Console.WriteLine(input);
        Console.WriteLine("\n" + new string('=', 50));

        // Test title pattern
        var titlePattern = @"(?:project\s*)?title\s*[:\s]+[""']?([^""',\n\r]+?)[""']?(?:\s*[,\n\r]|$)";
        var titleMatch = Regex.Match(input, titlePattern, RegexOptions.IgnoreCase);
        Console.WriteLine($"Title Match: {titleMatch.Success}");
        if (titleMatch.Success)
        {
            Console.WriteLine($"Title Value: '{titleMatch.Groups[1].Value.Trim().Trim('"', '\'')}'");
        }

        // Test description pattern
        var descPattern = @"(?:project\s*)?description\s*[:\s]+[""']?([^""',\n\r]+?)[""']?(?:\s*[,\n\r]|$)";
        var descMatch = Regex.Match(input, descPattern, RegexOptions.IgnoreCase);
        Console.WriteLine($"Description Match: {descMatch.Success}");
        if (descMatch.Success)
        {
            Console.WriteLine($"Description Value: '{descMatch.Groups[1].Value.Trim().Trim('"', '\'')}'");
        }

        // Test email pattern
        var emailPattern = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b";
        var emailMatch = Regex.Match(input, emailPattern, RegexOptions.IgnoreCase);
        Console.WriteLine($"Email Match: {emailMatch.Success}");
        if (emailMatch.Success)
        {
            Console.WriteLine($"Email Value: '{emailMatch.Value}'");
        }

        // Test email field pattern
        var emailFieldPattern = @"email\s*[:\s]+[""']?([A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,})[""']?";
        var emailFieldMatch = Regex.Match(input, emailFieldPattern, RegexOptions.IgnoreCase);
        Console.WriteLine($"Email Field Match: {emailFieldMatch.Success}");
        if (emailFieldMatch.Success)
        {
            Console.WriteLine($"Email Field Value: '{emailFieldMatch.Groups[1].Value.Trim().Trim('"', '\'')}'");
        }

        // Test budget pattern - more specific
        var budgetPattern = @"approx\s*total\s*budget\s*[:\s]+(\d+(?:,\d{3})*(?:\.\d{2})?)|budget\s*[:\s]+\$?(\d+(?:,\d{3})*(?:\.\d{2})?)";
        var budgetMatch = Regex.Match(input, budgetPattern, RegexOptions.IgnoreCase);
        Console.WriteLine($"Budget Match: {budgetMatch.Success}");
        if (budgetMatch.Success)
        {
            var budgetValue = "";
            for (int i = 1; i < budgetMatch.Groups.Count; i++)
            {
                if (!string.IsNullOrEmpty(budgetMatch.Groups[i].Value))
                {
                    budgetValue = budgetMatch.Groups[i].Value.Replace(",", "");
                    break;
                }
            }
            Console.WriteLine($"Budget Value: '{budgetValue}'");
            Console.WriteLine($"All groups: {string.Join(", ", budgetMatch.Groups.Cast<Group>().Select((g, i) => $"Group{i}: '{g.Value}'"))}");
        }
    }
}
