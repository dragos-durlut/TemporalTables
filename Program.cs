using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.EntityFrameworkCore;

public class Program
{
    public static void Main()
    {
        var timestamps = Seed(new TimeSpan(0, 0, 1));
        
        LookupCurrentPrice("DeLorean");
        
        LookupPrices("DeLorean", timestamps[1], timestamps[3]);

        FindOrder("Arthur", timestamps[3]);

        DeleteCustomer("Arthur");

        QueryCustomerAndOrderSnapshots();

        RestoreCustomer("Arthur");

        QueryCustomerAndOrderSnapshots();

        QueryEverythingTemporalAsOf();
    }

    private static void LookupCurrentPrice(string productName)
    {
        using var context = new OrdersContext();

        var product = context.Products.Single(product => product.Name == productName);
        
        Console.WriteLine($"The '{product.Name}' with PK {product.Id} is currently ${product.Price}.");

        Console.WriteLine();
    }

    private static void LookupPrices(string productName, DateTime from, DateTime to)
    {
        using var context = new OrdersContext();

        Console.WriteLine($"Historical prices for {productName} from {from} to {to}:");

        var productSnapshots = context.Products
            .TemporalFromTo(from, to)
            .OrderBy(product => EF.Property<DateTime>(product, "PeriodStart"))
            .Where(product => product.Name == productName)
            .Select(product =>
                new
                {
                    Product = product,
                    PeriodStart = EF.Property<DateTime>(product, "PeriodStart"),
                    PeriodEnd = EF.Property<DateTime>(product, "PeriodEnd")
                })
            .ToList();

        foreach (var snapshot in productSnapshots)
        {
            Console.WriteLine(
                $"  The '{snapshot.Product.Name}' with PK {snapshot.Product.Id} was ${snapshot.Product.Price} from {snapshot.PeriodStart} until {snapshot.PeriodEnd}.");
        }

        Console.WriteLine();
    }

    private static void FindOrder(string customerName, DateTime on)
    {
        using var context = new OrdersContext(log: true);

        var order = context.Orders
            .TemporalAsOf(on)
            .Include(e => e.Product)
            .Include(e => e.Customer)
            .Single(order =>
                order.Customer.Name == customerName
                && order.OrderDate > on.Date
                && order.OrderDate < on.Date.AddDays(1));

        Console.WriteLine();

        Console.WriteLine(
            $"{order.Customer.Name} ordered a {order.Product.Name} for ${order.Product.Price} on {order.OrderDate}");

        Console.WriteLine();
    }

    private static void DeleteCustomer(string customerName)
    {
        using var context = new OrdersContext();

        var customer = context.Customers
            .Include(e => e.Orders)
            .Single(customer => customer.Name == customerName);
        
        context.RemoveRange(customer.Orders);
        context.Remove(customer);
        context.SaveChanges();
    }

    private static void RestoreCustomer(string customerName)
    {
        using var context = new OrdersContext(log: true);

        var customerDeletedOn = context.Customers
            .TemporalAll()
            .Where(customer => customer.Name == customerName)
            .OrderBy(customer => EF.Property<DateTime>(customer, "PeriodEnd"))
            .Select(customer => EF.Property<DateTime>(customer, "PeriodEnd"))
            .Last();
        
        Console.WriteLine();

        var customerAndOrders = context.Customers
            .TemporalAsOf(customerDeletedOn.AddMilliseconds(-1))
            .Include(e => e.Orders)
            .Single();
        
        Console.WriteLine();

        context.Add(customerAndOrders);
        context.SaveChanges();
        
        Console.WriteLine();
    }

    private static void QueryCustomerAndOrderSnapshots()
    {
        using var context = new OrdersContext(log: true);

        var customerSnapshots = context.Customers
            .TemporalAll()
            .OrderBy(customer => EF.Property<DateTime>(customer, "PeriodStart"))
            .Select(customer =>
                new
                {
                    Customer = customer,
                    PeriodStart = EF.Property<DateTime>(customer, "PeriodStart"),
                    PeriodEnd = EF.Property<DateTime>(customer, "PeriodEnd")
                })
            .ToList();

        foreach (var snapshot in customerSnapshots)
        {
            Console.WriteLine(
                $"The customer '{snapshot.Customer.Name}' existed from {snapshot.PeriodStart} until {snapshot.PeriodEnd}.");
        }

        Console.WriteLine();

        var orderSnapshots = context.Orders
            .TemporalAll()
            .OrderBy(order => EF.Property<DateTime>(order, "PeriodStart"))
            .Select(order =>
                new
                {
                    Order = order,
                    PeriodStart = EF.Property<DateTime>(order, "PeriodStart"),
                    PeriodEnd = EF.Property<DateTime>(order, "PeriodEnd")
                })
            .ToList();

        foreach (var snapshot in orderSnapshots)
        {
            Console.WriteLine(
                $"The order with ID '{snapshot.Order.Id}' existed from {snapshot.PeriodStart} until {snapshot.PeriodEnd}.");
        }

        Console.WriteLine();
    }

    private static void QueryEverythingTemporalAsOf()
    {
        using var context = new OrdersContext(log: true);

        var query = context.Products.TemporalAsOf(DateTime.UtcNow)
            .Include(p => p.Orders).ThenInclude(o => o.Customer)
            .Include(p => p.ProductType).ThenInclude(pt => pt.ProductClass);

        var list = query.ToList();
    }

    private static List<DateTime> Seed(TimeSpan sleep)
    {
        using var context = new OrdersContext();

        context.Database.EnsureDeleted();
        context.Database.Migrate();

        var productClasses = new List<ProductClass>()
        {
            new ProductClass() { Id = 111, Name = "Product Class 1" },
            new ProductClass() { Id = 112, Name = "Product Class 2" },
            new ProductClass() { Id = 113, Name = "Product Class 3" }
        };

        context.ProductClasses.AddRange(productClasses);


        var productTypes = new List<ProductType>() 
        {
            new ProductType() { Id = 11, Name = "Product Type 11", ProductClass = productClasses[0] },
            new ProductType() { Id = 12, Name = "Product Type 12", ProductClass = productClasses[1] },
            new ProductType() { Id = 13, Name = "Product Type 13", ProductClass = productClasses[2] },

            new ProductType() { Id = 21, Name = "Product Type 21", ProductClass = productClasses[0] },
            new ProductType() { Id = 22, Name = "Product Type 22", ProductClass = productClasses[1] },
            new ProductType() { Id = 23, Name = "Product Type 23", ProductClass = productClasses[2] },

            new ProductType() { Id = 31, Name = "Product Type 31", ProductClass = productClasses[0] },
            new ProductType() { Id = 32, Name = "Product Type 32", ProductClass = productClasses[1] },
            new ProductType() { Id = 33, Name = "Product Type 33", ProductClass = productClasses[2] }
        };

        context.ProductTypes.AddRange(productTypes);

        var timestamps = new List<DateTime>();

        var customer = new Customer { Name = "Arthur" };
        context.Customers.Add(customer);

        var products = new List<Product>
        {
            new() { Name = "DeLorean", Price = 1_000_000.00m, ProductType = productTypes[0] },
            new() { Name = "Flux Capacitor", Price = 666.00m, ProductType = productTypes[1] },
            new() { Name = "Hoverboard", Price = 59_000.00m, ProductType = productTypes[2] }
        };

        context.AddRange(products);

        context.SaveChanges();
        timestamps.Add(DateTime.UtcNow);
        Thread.Sleep(sleep);

        products[0].Price = 2_000_000.00m;

        context.SaveChanges();
        timestamps.Add(DateTime.UtcNow);
        Thread.Sleep(sleep);

        products[0].Price = 2_500_000.00m;

        context.SaveChanges();
        timestamps.Add(DateTime.UtcNow);
        Thread.Sleep(sleep);
        
        context.Add(new Order { Customer = customer, Product = products[0], OrderDate = timestamps.Last() });

        context.SaveChanges();
        timestamps.Add(DateTime.UtcNow);
        Thread.Sleep(sleep);

        products[0].Price = 75_000.00m;

        context.SaveChanges();
        timestamps.Add(DateTime.UtcNow);
        Thread.Sleep(sleep);
        
        products[0].Price = 150_000.00m;

        context.SaveChanges();
        timestamps.Add(DateTime.UtcNow);
        Thread.Sleep(sleep);

        return timestamps;
    }
}