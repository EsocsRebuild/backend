using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Modules.Tenancy.Domain;
using Platform.Modules.Tenancy.Infrastructure;

namespace Platform.Modules.Tenancy.Features;

public sealed record ProvinceHierarchyResponse(
    string Id,
    string Name,
    string ShortCode,
    IReadOnlyList<DistrictHierarchyResponse> Districts);

public sealed record DistrictHierarchyResponse(
    string Id,
    string Name,
    IReadOnlyList<ParishBranchResponse> Branches);

public sealed record ParishBranchResponse(
    string Id,
    string Name,
    IReadOnlyList<string> HousesOfPrayer);

public sealed record HierarchyTreeResponse(
    IReadOnlyList<ProvinceHierarchyResponse> Hierarchy,
    IReadOnlyList<string> Provinces,
    int TotalProvinces);

public sealed record HierarchyPostRequest(
    string Action,
    IReadOnlyList<ProvinceHierarchyResponse>? Hierarchy = null,
    ProvinceHierarchyResponse? Province = null,
    string? ProvinceId = null,
    string? DistrictId = null,
    DistrictHierarchyResponse? District = null,
    string? BranchId = null,
    ParishBranchResponse? Branch = null,
    string? PerformedBy = null);

/// <summary>
/// Ecclesiastical organizational hierarchy tree (Provinces -> Districts -> Branches -> Houses of Prayer).
/// Serves the Ordination Portal, Admin Portal, and Web platform directory.
/// </summary>
public static class HierarchyEndpoints
{
    private static readonly ConcurrentBag<ProvinceHierarchyResponse> CustomHierarchy = [];

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/hierarchy", GetHierarchy)
            .AllowAnonymous()
            .WithTags("Organisation")
            .WithSummary("Ecclesiastical hierarchy tree");

        endpoints.MapPost("/hierarchy", PostHierarchy)
            .AllowAnonymous()
            .WithTags("Organisation")
            .WithSummary("Update ecclesiastical hierarchy tree");
    }

    private static async Task<IResult> GetHierarchy(TenancyDbContext db, CancellationToken ct)
    {
        // 1. If database contains provinces/districts/branches, construct from database
        var dbProvinces = await db.Units.AsNoTracking()
            .Where(u => u.Kind == UnitKind.Province || u.Kind == UnitKind.Cmc)
            .OrderBy(u => u.SortOrder).ThenBy(u => u.Name)
            .ToListAsync(ct);

        if (dbProvinces.Count > 0)
        {
            var provinceIds = dbProvinces.Select(p => p.Id).ToList();
            var dbDistricts = await db.Units.AsNoTracking()
                .Where(u => u.Kind == UnitKind.District && u.ParentId.HasValue && provinceIds.Contains(u.ParentId.Value))
                .OrderBy(u => u.SortOrder).ThenBy(u => u.Name)
                .ToListAsync(ct);

            var districtIds = dbDistricts.Select(d => d.Id).ToList();
            var dbBranches = await db.Units.AsNoTracking()
                .Where(u => u.Kind == UnitKind.Branch && u.ParentId.HasValue && districtIds.Contains(u.ParentId.Value))
                .OrderBy(u => u.SortOrder).ThenBy(u => u.Name)
                .ToListAsync(ct);

            var hierarchy = dbProvinces.Select(p => new ProvinceHierarchyResponse(
                p.Id.ToString(),
                p.Name,
                p.Slug.Length >= 3 ? p.Slug[..3].ToUpperInvariant() : p.Slug.ToUpperInvariant(),
                dbDistricts.Where(d => d.ParentId == p.Id).Select(d => new DistrictHierarchyResponse(
                    d.Id.ToString(),
                    d.Name,
                    dbBranches.Where(b => b.ParentId == d.Id).Select(b => new ParishBranchResponse(
                        b.Id.ToString(),
                        b.Name,
                        b.About ?? []
                    )).ToList()
                )).ToList()
            )).ToList();

            return Results.Ok(new HierarchyTreeResponse(
                hierarchy,
                hierarchy.Select(h => h.Name).ToList(),
                hierarchy.Count));
        }

        // 2. Return custom or canonical ESOCS hierarchy
        var activeHierarchy = CustomHierarchy.IsEmpty ? DefaultHierarchy : CustomHierarchy.ToList();
        return Results.Ok(new HierarchyTreeResponse(
            activeHierarchy,
            activeHierarchy.Select(h => h.Name).ToList(),
            activeHierarchy.Count));
    }

    private static IResult PostHierarchy(HierarchyPostRequest body)
    {
        if (body.Action.Equals("save_all", StringComparison.OrdinalIgnoreCase) && body.Hierarchy is not null)
        {
            CustomHierarchy.Clear();
            foreach (var p in body.Hierarchy)
            {
                CustomHierarchy.Add(p);
            }

            var updated = CustomHierarchy.ToList();
            return Results.Ok(new { hierarchy = updated, message = "Hierarchy saved successfully." });
        }

        return Results.Ok(new { hierarchy = DefaultHierarchy, message = "Operation completed." });
    }

    private static readonly IReadOnlyList<ProvinceHierarchyResponse> DefaultHierarchy =
    [
        new(
            "prov_lagos_central",
            "Lagos Central Province",
            "LCP",
            [
                new(
                    "dist_ebute_metta",
                    "Ebute Metta District (Apex Mother District)",
                    [
                        new("br_mount_zion_hq", "Mount Zion Cathedral Branch (National Headquarters)", [
                            "Holy Ark Main House of Prayer",
                            "Mount Zion General Conclave Sanctuary",
                            "St. Moses Orimolade Memorial Chapel",
                            "Zion Deliverance Altar"
                        ]),
                        new("br_oyingbo", "Oyingbo Branch", [
                            "Oyingbo Central House of Prayer",
                            "Adekunle Prayer Altar",
                            "Coates Sanctuary"
                        ]),
                        new("br_coates", "Coates Street Branch", [
                            "Coates Chapel of Glory",
                            "Freeman Prayer Center"
                        ])
                    ]
                ),
                new(
                    "dist_surulere",
                    "Surulere District",
                    [
                        new("br_surulere_hq", "Surulere Headquarters Cathedral Branch", [
                            "Mount Zion Main House of Prayer",
                            "Bethel Chapel of Praise",
                            "Salem Sanctuary of Grace",
                            "Moriah House of Deliverance"
                        ]),
                        new("br_aguda", "Aguda Branch", [
                            "Aguda House of Deliverance",
                            "Peniel Sanctuary of Peace",
                            "En-Rogel Prayer Altar"
                        ]),
                        new("br_itire_ijesha", "Itire / Ijesha Branch", [
                            "Ijesha House of Grace",
                            "Itire Altar of Hope",
                            "Lawanson Chapel"
                        ])
                    ]
                ),
                new(
                    "dist_lagos_island",
                    "Lagos Island / Apapa District",
                    [
                        new("br_lagos_island", "Lagos Island Branch (King George)", [
                            "King George House of Prayer",
                            "Isale Eko Chapel of Light",
                            "Broad Street Sanctuary"
                        ]),
                        new("br_apapa_marine", "Apapa Marine Branch", [
                            "Apapa Sanctuary of Deliverance",
                            "Wharf Fellowship Chapel"
                        ])
                    ]
                )
            ]
        ),
        new(
            "prov_lagos_mainland_1",
            "Lagos Mainland 1 Province",
            "LM1",
            [
                new(
                    "dist_yaba_alagomeji",
                    "Yaba / Alagomeji District",
                    [
                        new("br_yaba_central", "Yaba Central Cathedral Branch", [
                            "Yaba House of Light",
                            "Commercial Avenue Chapel",
                            "Tejuosho Prayer Sanctuary"
                        ]),
                        new("br_alagomeji", "Alagomeji Branch", [
                            "Alagomeji Altar of Grace",
                            "Herbert Macaulay Chapel"
                        ])
                    ]
                ),
                new(
                    "dist_somolu_bariga",
                    "Somolu / Bariga District",
                    [
                        new("br_somolu_central", "Somolu Central Branch", [
                            "Somolu Altar of Victory",
                            "Fagbile Chapel of Praise"
                        ]),
                        new("br_bariga", "Bariga Branch", [
                            "Bariga Altar of Deliverance",
                            "Ilaje Sanctuary"
                        ])
                    ]
                )
            ]
        ),
        new(
            "prov_rivers",
            "Rivers Province",
            "RVP",
            [
                new(
                    "dist_port_harcourt",
                    "Port Harcourt Township District",
                    [
                        new("br_diobu", "Diobu Provincial Headquarters Branch", [
                            "Diobu Main Sanctuary",
                            "Mile 1 Chapel of Praise",
                            "Mile 3 Altar of Grace"
                        ]),
                        new("br_trans_amadi", "Trans-Amadi Branch", [
                            "Trans-Amadi House of Light",
                            "Industrial Layout Chapel"
                        ])
                    ]
                )
            ]
        ),
        new(
            "prov_fct_abuja",
            "Federal Capital Territory Province",
            "FCT",
            [
                new(
                    "dist_abuja_central",
                    "Abuja Central District",
                    [
                        new("br_garki_cathedral", "Garki Central Cathedral Branch", [
                            "Garki Main Altar",
                            "Area 1 House of Prayer",
                            "Area 10 Chapel of Glory"
                        ]),
                        new("br_wuse", "Wuse Branch", [
                            "Wuse House of Grace",
                            "Zone 4 Prayer Sanctuary"
                        ])
                    ]
                )
            ]
        ),
        new(
            "prov_uk_europe",
            "United Kingdom & Europe Province",
            "UKE",
            [
                new(
                    "dist_london_central",
                    "London Central District",
                    [
                        new("br_chatham_london", "London Central Cathedral Branch", [
                            "Chatham Street Main House of Prayer",
                            "Southwark Chapel of Light",
                            "Elephant & Castle Prayer Altar"
                        ])
                    ]
                )
            ]
        ),
        new(
            "prov_north_america",
            "North America Province",
            "USA",
            [
                new(
                    "dist_usa_east",
                    "USA Eastern District",
                    [
                        new("br_new_york", "New York Cathedral Branch", [
                            "Brooklyn Main House of Prayer",
                            "Queens Chapel of Deliverance"
                        ]),
                        new("br_houston", "Houston Texas Branch", [
                            "Houston House of Grace",
                            "Sugar Land Sanctuary"
                        ])
                    ]
                )
            ]
        )
    ];
}

