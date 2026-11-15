using System.Text.Json;
using ZekPcSayac.Core;

namespace ZekPcSayac.Core.Tests;

public class AllowedWindowParserTests
{
    private static string Day(int day) => $"{{\"integerValue\":\"{day}\"}}";

    private static string Item(string daysValues, string start, string end) =>
        "{\"mapValue\":{\"fields\":{" +
        $"\"days\":{{\"arrayValue\":{{\"values\":[{daysValues}]}}}}," +
        $"\"start\":{{\"stringValue\":\"{start}\"}}," +
        $"\"end\":{{\"stringValue\":\"{end}\"}}" +
        "}}}";

    private static string ArrayValue(params string[] items) =>
        $"{{\"values\":[{string.Join(",", items)}]}}";

    private static (List<AllowedWindow> Windows, int Skipped) ParseJson(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return AllowedWindowParser.Parse(doc.RootElement);
    }

    [Fact]
    public void Gecerli_oge_dogru_okunur()
    {
        string json = ArrayValue(Item(Day(1) + "," + Day(3), "21:00", "00:30"));

        (List<AllowedWindow> windows, int skipped) = ParseJson(json);

        Assert.Equal(0, skipped);
        AllowedWindow window = Assert.Single(windows);
        Assert.Equal(new[] { 1, 3 }, window.Days);
        Assert.Equal(new TimeOnly(21, 0), window.Start);
        Assert.Equal(new TimeOnly(0, 30), window.End);
    }

    [Fact]
    public void Gunler_siralanir_ve_tekrarlar_atilir()
    {
        string json = ArrayValue(Item(Day(5) + "," + Day(1) + "," + Day(5), "09:00", "17:00"));

        (List<AllowedWindow> windows, _) = ParseJson(json);

        Assert.Equal(new[] { 1, 5 }, Assert.Single(windows).Days);
    }

    [Fact]
    public void Bos_veya_eksik_values_bos_liste_doner()
    {
        Assert.Empty(ParseJson("{}").Windows);
        Assert.Equal(0, ParseJson("{}").Skipped);
        Assert.Empty(ParseJson("{\"values\":[]}").Windows);
        Assert.Empty(ParseJson("null").Windows);
    }

    [Fact]
    public void Gecersiz_saat_bicimi_atlanir()
    {
        string json = ArrayValue(
            Item(Day(1), "9:00", "17:00"),
            Item(Day(1), "25:00", "26:00"),
            Item(Day(1), "09:00", "bozuk"));

        (List<AllowedWindow> windows, int skipped) = ParseJson(json);

        Assert.Empty(windows);
        Assert.Equal(3, skipped);
    }

    [Fact]
    public void Gecersiz_gun_numarasi_atlanir()
    {
        string json = ArrayValue(
            Item(Day(8), "09:00", "17:00"),
            Item(Day(0), "09:00", "17:00"),
            Item(Day(1), "09:00", "17:00"));

        (List<AllowedWindow> windows, int skipped) = ParseJson(json);

        Assert.Single(windows);
        Assert.Equal(2, skipped);
    }

    [Fact]
    public void Baslangic_ve_bitis_esitse_atlanir()
    {
        string json = ArrayValue(Item(Day(2), "10:00", "10:00"));

        (List<AllowedWindow> windows, int skipped) = ParseJson(json);

        Assert.Empty(windows);
        Assert.Equal(1, skipped);
    }

    [Fact]
    public void Bos_gun_listesi_atlanir()
    {
        string json = ArrayValue(Item("", "09:00", "17:00"));

        (List<AllowedWindow> windows, int skipped) = ParseJson(json);

        Assert.Empty(windows);
        Assert.Equal(1, skipped);
    }

    [Fact]
    public void Gun_alani_eksikse_atlanir()
    {
        string json = ArrayValue(
            "{\"mapValue\":{\"fields\":{\"start\":{\"stringValue\":\"09:00\"},\"end\":{\"stringValue\":\"17:00\"}}}}");

        (List<AllowedWindow> windows, int skipped) = ParseJson(json);

        Assert.Empty(windows);
        Assert.Equal(1, skipped);
    }

    [Fact]
    public void Gecersiz_ve_gecerli_ogeler_birlikte_islenir()
    {
        string json = ArrayValue(
            Item(Day(1), "21:00", "00:30"),
            Item(Day(9), "09:00", "17:00"),
            Item(Day(2), "08:00", "12:00"));

        (List<AllowedWindow> windows, int skipped) = ParseJson(json);

        Assert.Equal(2, windows.Count);
        Assert.Equal(1, skipped);
    }
}
