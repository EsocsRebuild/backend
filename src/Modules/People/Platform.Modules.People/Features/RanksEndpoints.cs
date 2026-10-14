using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Platform.Modules.People.Features;

public sealed record MandatoryLevyBreakdown(
    decimal BranchLevy,
    decimal DistrictLevy,
    decimal ProvincialLevy,
    decimal NationalFee,
    decimal Total);

public sealed record EcclesiasticalRankResponse(
    string Id,
    string Name,
    string ShortCode,
    int OrderLevel,
    string GenderEligibility,
    string? PrerequisiteRankId,
    string? PrerequisiteRankName,
    int MinimumYearsInCurrentRank,
    string RobingCategory,
    string LiturgicalColor,
    string BadgeColor,
    IReadOnlyList<string> TheologicalRequirements,
    string Description,
    MandatoryLevyBreakdown LevyBreakdown);

public sealed record RanksListResponse(
    IReadOnlyList<EcclesiasticalRankResponse> Ranks,
    int TotalRanks);

public sealed record RanksPostRequest(
    string Action,
    IReadOnlyList<EcclesiasticalRankResponse>? Ranks = null,
    string? RankId = null,
    EcclesiasticalRankResponse? Rank = null,
    string? PerformedBy = null);

/// <summary>
/// Ecclesiastical Holy Order ranks, robes, and ordination requirements.
/// Serves the Ordination Portal, Admin Portal, and member directory.
/// </summary>
public static class RanksEndpoints
{
    private static readonly ConcurrentBag<EcclesiasticalRankResponse> CustomRanks = [];

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/ranks", GetRanks)
            .AllowAnonymous()
            .WithTags("People")
            .WithSummary("Ecclesiastical ranks and holy orders");

        endpoints.MapPost("/ranks", PostRanks)
            .AllowAnonymous()
            .WithTags("People")
            .WithSummary("Update ecclesiastical ranks and holy orders");
    }

    private static IResult GetRanks(string? gender)
    {
        var allRanks = CustomRanks.IsEmpty ? DefaultRanks : CustomRanks.ToList();

        if (!string.IsNullOrWhiteSpace(gender))
        {
            var normalizedGender = gender.Trim().ToLowerInvariant();
            allRanks = allRanks.Where(r =>
                r.GenderEligibility.Equals("both", StringComparison.OrdinalIgnoreCase) ||
                r.GenderEligibility.Equals(normalizedGender, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return Results.Ok(new RanksListResponse(allRanks, allRanks.Count));
    }

    private static IResult PostRanks(RanksPostRequest body)
    {
        if (body.Action.Equals("save_all", StringComparison.OrdinalIgnoreCase) && body.Ranks is not null)
        {
            CustomRanks.Clear();
            foreach (var r in body.Ranks)
            {
                CustomRanks.Add(r);
            }

            var updated = CustomRanks.ToList();
            return Results.Ok(new { ranks = updated, message = "Ecclesiastical ranks saved successfully." });
        }

        return Results.Ok(new { ranks = DefaultRanks, message = "Operation completed." });
    }

    private static readonly IReadOnlyList<EcclesiasticalRankResponse> DefaultRanks =
    [
        // --- MALE ORDERS (12) ---
        new(
            "rank_brother", "Brother", "BRO", 0, "male", null, null, 1,
            "White_Gold", "White", "bg-slate-100 text-slate-800 border-slate-300 dark:bg-slate-800 dark:text-slate-200",
            ["Baptismal Catechism", "Order of Holy Services & C&S Doctrine"],
            "Baptized faithful brother of the Holy Order.",
            new(5000, 5000, 5000, 10000, 25000)),

        new(
            "rank_aladura_male", "Aladura", "ALD", 1, "male", "rank_brother", "Brother", 2,
            "White_Gold", "White", "bg-slate-100 text-slate-800 border-slate-300 dark:bg-slate-800 dark:text-slate-200",
            ["Basic Liturgy 101", "Foundations of Prayer & Altar Sanctification"],
            "Foundational holy order of prayer, intercession, and sanctuary preparation.",
            new(7000, 7000, 8000, 13000, 35000)),

        new(
            "rank_leader_male", "Leader", "LDR", 2, "male", "rank_aladura_male", "Aladura", 2,
            "White_Gold", "White", "bg-slate-100 text-slate-800 border-slate-300 dark:bg-slate-800 dark:text-slate-200",
            ["Leadership in the Sanctuary", "Order of Divine Services", "Pastoral Stewardship"],
            "Parish leader assisting elders in conducting services and pastoral coordination.",
            new(9000, 9000, 12000, 15000, 45000)),

        new(
            "rank_rabbi", "Rabbi", "RAB", 3, "male", "rank_leader_male", "Leader", 3,
            "White_Gold", "White", "bg-slate-100 text-slate-800 border-slate-300 dark:bg-slate-800 dark:text-slate-200",
            ["Biblical Hermeneutics", "Teacher in Israel Colloquium", "Spiritual Warfare in Scripture"],
            "Teachers and instructors of biblical doctrine, catechism, and sacred liturgical traditions.",
            new(10000, 10000, 15000, 20000, 55000)),

        new(
            "rank_pastor", "Pastor", "PST", 4, "male", "rank_rabbi", "Rabbi", 3,
            "White_Gold", "White", "bg-amber-100 text-amber-900 border-amber-300 dark:bg-amber-950/60 dark:text-amber-200",
            ["Pastoral Care & Homiletics", "Ecclesiastical Counseling", "Parish Administration"],
            "Shepherds of the local flock, preaching the Word and guiding parish members.",
            new(12000, 12000, 18000, 23000, 65000)),

        new(
            "rank_evangelist", "Evangelist", "EVG", 5, "male", "rank_pastor", "Pastor", 3,
            "White_Gold", "White", "bg-amber-100 text-amber-900 border-amber-300 dark:bg-amber-950/60 dark:text-amber-200",
            ["Great Commission & Missions", "Revival Ministry", "Apologetics & Outreach"],
            "Proclaimers of the Holy Gospel, revivalists, and missionary pioneers.",
            new(15000, 15000, 20000, 30000, 80000)),

        new(
            "rank_apostle_white", "Apostle (White)", "APO", 6, "male", "rank_evangelist", "Evangelist", 4,
            "White", "White", "bg-slate-100 text-slate-900 border-slate-400 font-bold dark:bg-slate-800 dark:text-white",
            ["Advanced Ecclesiology", "Church Administration", "Liturgical Governance"],
            "Foundational apostolic pillar with White liturgical stole, supervisory authority over divine services.",
            new(18000, 18000, 24000, 35000, 95000)),

        new(
            "rank_super_apostle_pink", "Super Apostle (Pink)", "SUA", 7, "male", "rank_apostle_white", "Apostle (White)", 4,
            "Pink", "Pink", "bg-pink-100 text-pink-900 border-pink-300 dark:bg-pink-950/60 dark:text-pink-200 font-bold",
            ["Apostolic Jurisdiction & Council Laws", "Synodical Liturgy", "District Supervisory Ethics"],
            "Super Apostle distinguished by Pink ceremonial liturgical sash, overseeing zonal parish developments.",
            new(20000, 20000, 30000, 45000, 115000)),

        new(
            "rank_senior_apostle_yellow", "Senior Apostle (Yellow)", "SAP", 8, "male", "rank_super_apostle_pink", "Super Apostle (Pink)", 4,
            "Yellow", "Yellow", "bg-yellow-100 text-yellow-900 border-yellow-400 dark:bg-yellow-950/60 dark:text-yellow-200 font-bold",
            ["Synodical Jurisprudence", "District Oversight & Episcopal Guidance", "Theology of Sacraments"],
            "Senior Apostle bearing Yellow/Gold bishopric stole, leading district governance and spiritual arbitration.",
            new(25000, 25000, 35000, 55000, 140000)),

        new(
            "rank_special_senior_apostle_blue", "Special Senior Apostle (Blue)", "SSA", 9, "male", "rank_senior_apostle_yellow", "Senior Apostle (Yellow)", 5,
            "Blue", "Blue", "bg-blue-100 text-blue-900 border-blue-400 dark:bg-blue-950/60 dark:text-blue-200 font-bold",
            ["Provincial Leadership Colloquium", "High Council Ethics & Governance", "Constitutional Law of Holy Order"],
            "Special Senior Apostle bearing Royal Blue velvet stole & gold trim, presiding over provincial dioceses and synod meetings.",
            new(30000, 30000, 45000, 65000, 170000)),

        new(
            "rank_apostle_general_green", "Apostle General (Green)", "APG", 10, "male", "rank_special_senior_apostle_blue", "Special Senior Apostle (Blue)", 5,
            "Green", "Green", "bg-emerald-100 text-emerald-900 border-emerald-400 dark:bg-emerald-950/60 dark:text-emerald-200 font-bold",
            ["Apex Spiritual Council Fellowship", "Holy Synod Supreme Ordinances", "International Mission Jurisprudence"],
            "Apostle General adorned in Emerald Green apex regalia, supreme council member advising His Most Eminence.",
            new(35000, 35000, 50000, 80000, 200000)),

        new(
            "rank_supervising_apostle_general_green", "Supervising Apostle General (Green)", "SAG", 11, "male", "rank_apostle_general_green", "Apostle General (Green)", 6,
            "Green", "Green", "bg-emerald-200 text-emerald-950 border-emerald-500 dark:bg-emerald-900/90 dark:text-emerald-100 font-extrabold shadow-sm",
            ["Supreme Head Advisory Conclave", "Apex Sovereign Ecclesiastical Doctorate", "Universal Synod Presidency"],
            "Supervising Apostle General bearing Emerald Green apex patriarchal regalia with gold fringe, commanding international diocesan oversight.",
            new(45000, 45000, 60000, 100000, 250000)),

        // --- FEMALE ORDERS (10) ---
        new(
            "rank_sister", "Sister", "SIS", 0, "female", null, null, 1,
            "White_Gold", "White", "bg-slate-100 text-slate-800 border-slate-300 dark:bg-slate-800 dark:text-slate-200",
            ["Baptismal Catechism", "Order of Holy Services & Sisterhood Devotion"],
            "Baptized faithful sister of the Holy Order.",
            new(5000, 5000, 5000, 10000, 25000)),

        new(
            "rank_lady_aladura", "Lady Aladura", "LAD", 1, "female", "rank_sister", "Sister", 2,
            "White_Gold", "White", "bg-slate-100 text-slate-800 border-slate-300 dark:bg-slate-800 dark:text-slate-200",
            ["Basic Liturgy 101", "Foundations of Prayer & Altar Sanctification"],
            "Foundational female order of prayer, intercession, and sanctuary preparation.",
            new(7000, 7000, 8000, 13000, 35000)),

        new(
            "rank_lady_leader", "Lady Leader", "LLD", 2, "female", "rank_lady_aladura", "Lady Aladura", 2,
            "White_Gold", "White", "bg-slate-100 text-slate-800 border-slate-300 dark:bg-slate-800 dark:text-slate-200",
            ["Women in Ministry Leadership", "Pastoral Care & Welfare", "Liturgical Order"],
            "Leader of the female fold, championing women fellowship, spiritual growth, and parish welfare.",
            new(9000, 9000, 12000, 15000, 45000)),

        new(
            "rank_dorcas", "Dorcas", "DCS", 3, "female", "rank_lady_leader", "Lady Leader", 3,
            "White_Gold", "White", "bg-amber-100 text-amber-900 border-amber-300 dark:bg-amber-950/60 dark:text-amber-200",
            ["Benevolence Ministry & Almsgiving", "Widows & Orphans Care", "Sanctuary Vestment Preparation"],
            "Holy Order of Dorcas, custodians of charity, benevolence, and sacred sanctuary garments.",
            new(10000, 10000, 15000, 20000, 55000)),

        new(
            "rank_deborah", "Deborah", "DBH", 4, "female", "rank_dorcas", "Dorcas", 3,
            "White_Gold", "White", "bg-amber-100 text-amber-900 border-amber-300 dark:bg-amber-950/60 dark:text-amber-200",
            ["Spiritual Warfare & Intercession", "Judicial Wisdom & Family Counseling", "Prophetic Song Ministry"],
            "Holy Order of Deborah, counselors, spiritual judges, and warriors of prayer in Israel.",
            new(12000, 12000, 18000, 23000, 65000)),

        new(
            "rank_mary", "Mary", "MRY", 5, "female", "rank_deborah", "Deborah", 3,
            "White_Gold", "White", "bg-purple-100 text-purple-900 border-purple-300 dark:bg-purple-950/60 dark:text-purple-200 font-bold",
            ["Theology of the Incarnation & Humility", "Maternal Intercession", "Sanctuary Sanctification"],
            "Holy Order of Mary, emblems of purity, devotion, and steadfast altar intercession.",
            new(15000, 15000, 20000, 30000, 80000)),

        new(
            "rank_prophetess", "Prophetess", "PRP", 6, "female", "rank_mary", "Mary", 4,
            "Purple_Gold", "Purple", "bg-purple-100 text-purple-900 border-purple-400 dark:bg-purple-950/60 dark:text-purple-200 font-bold",
            ["Biblical Pneumatology & Prophecy", "Spiritual Discernment & Visions", "Order of Holy Mount Vigils"],
            "Holy Order of Prophetess, prophetic oracles, spiritual seers, and prayer vigil leaders.",
            new(18000, 18000, 24000, 35000, 95000)),

        new(
            "rank_mother_in_israel", "Mother in Israel", "MII", 7, "female", "rank_prophetess", "Prophetess", 4,
            "Purple_Gold", "Purple", "bg-purple-200 text-purple-950 border-purple-400 dark:bg-purple-900/80 dark:text-purple-100 font-bold",
            ["Matriarchal Ecclesiology", "Matron Counseling & Family Mediation", "Sanctuary Protocol Governance"],
            "Matriarch of the Holy Order, advisor on family, counseling, and district prayer altars.",
            new(20000, 20000, 30000, 45000, 115000)),

        new(
            "rank_senior_mother_in_israel", "Snr. Mother in Israel", "SMI", 8, "female", "rank_mother_in_israel", "Mother in Israel", 4,
            "Red_Gold", "Crimson", "bg-rose-100 text-rose-900 border-rose-400 dark:bg-rose-950/60 dark:text-rose-200 font-bold",
            ["Provincial Matron Synod Leadership", "Holy Mount Sacred Ordinances", "Advanced Ecclesiastical Ethics"],
            "Senior provincial matriarch, custodian of spiritual counsel and provincial women leadership.",
            new(25000, 25000, 35000, 55000, 140000)),

        new(
            "rank_special_senior_mother_in_israel", "Sp. Snr. Mother in Israel", "SSMI", 9, "female", "rank_senior_mother_in_israel", "Snr. Mother in Israel", 5,
            "Red_Gold", "Crimson", "bg-rose-200 text-rose-950 border-rose-500 dark:bg-rose-900/90 dark:text-rose-100 font-extrabold shadow-sm",
            ["Supreme Matron Advisory Conclave", "Apex Ecclesiastical Matriarchy", "Universal Council Presidency"],
            "Special Senior Mother in Israel bearing Crimson/Gold apex regalia, supreme advisory conclave matriarch.",
            new(30000, 30000, 45000, 65000, 170000))
    ];
}

