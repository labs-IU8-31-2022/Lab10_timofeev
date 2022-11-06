using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.EntityFrameworkCore;
using yahoo;
using System.Diagnostics;

namespace YahooFinanceDB
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(
                XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
            DataContext = this;
            TicksSorted = new List<Ticker>();
            Task.Run(InitializeDb);
        }

        public List<Ticker> TicksSorted { get; set; }

        public Ticker? SelectedTicker { get; set; }


        private void GetState(object sender, RoutedEventArgs e)
        {
            if (SelectedTicker is null)
            {
                ComboBox1.Text = "Выберите актив!!!";
                return;
            }

            Button1.IsEnabled = false;
            var task = Task.Run(() =>
            {
                var db = new Application();
                var tickId = SelectedTicker.TickerId;
                var state = db.Conditions.First(cond => cond.TickerId.Equals(tickId)).State switch
                {
                    > 0 => "вырос",
                    < 0 => "упал",
                    0 => "не изменился"
                };

                if (state == "не изменился")
                {
                    return $"Актив {db.Tickers.Find(tickId)!.Name} {state}\nЦена осталась на уровне  " +
                           $"{db.Prices.Where(price => price.TickerId.Equals(tickId)).Select(price => price.Value[0].Val).First():f4}$";
                }
                return $"Актив {db.Tickers.Find(tickId)!.Name} {state}\nс " +
                       $"{db.Prices.Where(price => price.TickerId.Equals(tickId)).Select(price => price.Value[1].Val).First():f4}$ до " +
                       $"{db.Prices.Where(price => price.TickerId.Equals(tickId)).Select(price => price.Value[0].Val).First():f4}$";
            });
            task.ContinueWith((t) =>
            {
                Dispatcher.Invoke(() =>
                {
                    Text1.Text = t.Result;
                    Button1.IsEnabled = true;
                });
            });
        }

        private int _count;

        private void Count(object sender, RoutedEventArgs e)
        {
            Text2.Text = $"{++_count} click";
        }

        private async void InitializeDb()
        {
            MessageBox.Show("Начата загрузка базы данных. Это займёт около 10 секунд");
            var quotations = new List<string>();
            var ticks = new List<Ticker>();
            var prices = new List<Price>();
            var waitHandler = new AutoResetEvent(true);

            using var reader = new StreamReader($"{Environment.CurrentDirectory}/../../../Resources/ticker.txt");
            while (await reader.ReadLineAsync() is { } line)
            {
                if (line != "")
                    quotations.Add(line);
            }

            reader.Close();


            Task.WaitAll(quotations.Select(action => Task.Factory.StartNew(() =>
            {
                try
                {
                    var response = Yahoo.GetData(action);
                    var array = Yahoo.TwoDays(response);
                    if (array is null) return;
                    var enumerable = array as decimal[] ?? array.ToArray();
                    var decimals = new List<Decimal>
                        { new() { Val = enumerable.First() }, new() { Val = enumerable.Last() } };

                    var tick = new Ticker { Name = action };
                    var price = new Price { Ticker = tick, Value = decimals };

                    waitHandler.WaitOne();
                    ticks.Add(tick);
                    prices.Add(price);
                    waitHandler.Set();
                }
                catch (HttpRequestException e)
                {
                    if (e.StatusCode == HttpStatusCode.NotFound)
                    {
                        Debug.WriteLine($"404 (Not Found)  {action} may be delisted");
                        return;
                    }

                    Debug.WriteLine($"{e.Message}  {action}");
                    if (e.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        Thread.Sleep(290000);
                    }

                    Thread.Sleep(10000);
                }
            })).ToArray());

            await using var db = new Application();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
            db.Tickers.AddRange(ticks.OrderBy(t => t.Name));
            db.Prices.AddRange(prices);
            await db.SaveChangesAsync();

            db.Conditions
                .AddRange(db.Prices
                    .Select(price => new TodaysCondition
                    {
                        State = price.Value[0].Val > price.Value[1].Val ? 1 :
                            price.Value[0].Val < price.Value[1].Val ? -1 : 0,
                        TickerId = price.TickerId
                    }));
            await db.SaveChangesAsync();
            /*db.Conditions
                .AddRange(db.Prices
                    .Select(price => new TodaysCondition
                    { State = price.Value[0].Val.CompareTo(price.Value[1].Val), 
                        TickerId = price.TickerId }));
            await db.SaveChangesAsync();*/

            TicksSorted.AddRange(db.Tickers);

            MessageBox.Show("Загрузка завершена");
        }

        public class Ticker
        {
            [Key] public int TickerId { get; set; }
            public string? Name { get; set; }
        }

        public class Price
        {
            [Key] public int PriceId { get; set; }
            public int TickerId { get; set; }
            public Ticker? Ticker { get; set; }
            public List<Decimal> Value { get; set; } = null!;
        }

        public class Decimal
        {
            [Key] public int DecimalId { get; set; }
            public decimal Val { get; set; }
        }

        public class TodaysCondition
        {
            [Key] public int TodaysConditionId { get; set; }
            public int TickerId { get; set; }
            public Ticker? Ticker { get; set; }
            public int State { get; set; }
        }

        public sealed class Application : DbContext
        {
            public DbSet<Ticker> Tickers { get; set; } = null!;
            public DbSet<Price> Prices { get; set; } = null!;
            public DbSet<TodaysCondition> Conditions { get; set; } = null!;


            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder.UseSqlite($"Data Source={Environment.CurrentDirectory}/../../../Resources/Yahoo.db");
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Price>()
                    .HasOne(d => d.Ticker)
                    .WithOne()
                    .OnDelete(DeleteBehavior.Cascade);
            }
        }
    }
}