# Tubes_Suruh_AI_Berpikir
**Tugas Besar Strategi Algoritma — Robocode Tank Royale**
Institut Teknologi Sumatera, 2026

---

## Deskripsi Singkat

Repository ini berisi implementasi bot Robocode Tank Royale menggunakan **Algoritma Greedy** dalam bahasa C# (.NET). Terdapat 1 bot utama dan 3 bot alternatif, masing-masing menggunakan heuristik greedy yang berbeda.

---

## Algoritma Greedy yang Diimplementasikan

### Bot Utama — RamFireJr (Nearest Enemy + Chase & Ram)
Bot memilih musuh dengan **jarak terdekat** sebagai target utama (greedy lokal: `argmin distance`), lalu mengejar dan menabrak (Chase & Ram). Penembakan hanya dilakukan saat jarak ≤ 120 unit dengan daya tembak maksimum (3.0), memastikan hit chance mendekati 100%. Strategi ini terbukti paling efektif berdasarkan hasil pengujian dengan total skor tertinggi (4389) dan 7 kemenangan dari 10 ronde.

### Alt-Bot 1 — SetOrbit (Multi-Criteria Greedy Score)
Memilih target berdasarkan **skor gabungan multi-kriteria**: kecepatan musuh, HP musuh, kedekatan dengan jarak ideal, umur data, dan bonus situasional. Pergerakan menggunakan sistem orbit adaptif yang mengevaluasi kandidat arah berdasarkan jarak dari tembok dan sudut lateral terhadap musuh.

### Alt-Bot 2 — KnapsackBot (Knapsack Efficiency Ratio)
Mengadaptasi prinsip **Fractional Knapsack**: setiap musuh memiliki Value (keuntungan membunuh) dan Cost (estimasi energi yang dihabiskan). Bot memilih target dengan rasio `Value / Cost` tertinggi. Daya tembak juga dipilih secara knapsack — gunakan daya minimum yang cukup untuk membunuh musuh yang hampir mati.

### Alt-Bot 3 — GreedyBot (Energy + Score Weighted Heuristic)
Memilih target berdasarkan **weighted sum** sederhana: `HeuristicScore = (EnemyEnergy × 1.0) + (EnemyScore × 0.5)`. Memiliki mode agresif otomatis saat energi diri turun di bawah 40, di mana bot selalu menembak dengan daya maksimum.

---

## Requirements

- [.NET SDK](https://dotnet.microsoft.com/download) versi 6.0 atau lebih baru
- [Robocode Tank Royale](https://robocode.dev/) (game engine yang telah dimodifikasi sesuai starter pack tugas)
- OS: Windows / Linux / macOS

---

## Instalasi & Build

### 1. Clone repository

```bash
git clone https://github.com/Damble0/Tubes_Suruh_ai_berpikir.git
cd Tubes_Suruh_ai_berpikir
```

### 2. Build bot utama

```bash
cd src/main-bot/RamFireJr
./RamFire.cmd
```

### 3. Build bot alternatif (opsional)

```bash
cd src/alternative-bots/Greedy
./Greedy.cmd

cd ../Knapsack
./Knapsack.cmd

cd ../SetOrbit
./SetOrbit.cmd
```

### 4. Jalankan Robocode Tank Royale

Pastikan game engine sudah berjalan
```bash
java -jar robocode-tankroyale-gui-0.30.0.jar
```

lalu jalankan bot dari folder hasil build:

```bash
dotnet run
```

Bot akan otomatis terhubung ke server Robocode Tank Royale secara lokal.

---

## Struktur Repository

```
Tubes_Suruh_AI_Berpikir/
├── src/
│   ├── main-bot/           # RamFireJr — bot utama
│   └── alternative-bots/
│       ├── alt-bot-1/      # SetOrbit
│       ├── alt-bot-2/      # KnapsackBot
│       └── alt-bot-3/      # GreedyBot
├── doc/
│   └── laporan.pdf   # Laporan lengkap
└── README.md
```

---

## Author

| Nama | NIM |
|------|-----|
| Dzaky Faris Al Faqih | 124140077 |
| Timothy Montoya Wilfried Mangapul Situngkir | 124140104 |
| Mohamad Rifky Putra | 124140074 |

**Program Studi Teknik Informatika — Institut Teknologi Sumatera**

---

## Tautan

- **Repository GitHub**: https://github.com/Damble0/Tubes_Suruh_ai_berpikir
- **Video Demo (YouTube)**: https://youtu.be/uhQaUEcglSQ
- **Dokumentasi Robocode**: https://robocode.dev/
