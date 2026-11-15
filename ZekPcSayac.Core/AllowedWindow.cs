namespace ZekPcSayac.Core;

/// <summary>
/// Kullanım izni olan bir zaman aralığı. Days: ISO gün numaraları (1=Pzt..7=Paz).
/// Start dahil, End hariç. End&lt;=Start ise aralık, başladığı günden sonraki güne taşar.
/// </summary>
public record AllowedWindow(int[] Days, TimeOnly Start, TimeOnly End);
