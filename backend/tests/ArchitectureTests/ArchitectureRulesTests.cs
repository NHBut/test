using System.Reflection;
using Xunit;

namespace ECommerce.ArchitectureTests;

public class ArchitectureRulesTests
{
    [Fact]
    public void Module_Assemblies_Should_Not_Directly_Reference_Other_Module_Infrastructure()
    {
        // Danh sách các assembly của module
        var moduleNames = new[] { "Identity", "Product", "Inventory", "Cart", "Order", "Payment", "Promotion", "Shipping", "Review", "Notification", "Search" };
        
        foreach (var m1 in moduleNames)
        {
            var projFile = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "src", "Modules", m1, $"ECommerce.Modules.{m1}.csproj");
            if (File.Exists(projFile))
            {
                var content = File.ReadAllText(projFile);
                foreach (var m2 in moduleNames)
                {
                    if (m1 != m2)
                    {
                        // Kiểm tra không có thẻ reference tới module khác trong csproj
                        Assert.DoesNotContain($"ECommerce.Modules.{m2}.csproj", content);
                    }
                }
            }
        }
    }
}
