namespace Ehr.Data.Orders;

// UC 1 Place an order (Tahseen Arnan): one thing that can be ordered, in a small synthetic catalog for each type, which
// the migration that made the table seeds (Seeded, through HasData). The codes are plain synthetic ones, SYN-...: the
// synthetic clinic records its medications without codes, and no licensed code set is used. Not PHI: grants only, read
// by both apps, and nothing at runtime changes it.
public class OrderCatalogItem
{
    public required string Code { get; set; }

    // One of OrderType.
    public required string Type { get; set; }

    // The item as clinicians read it, unique within its type: what the order form suggests and takes.
    public required string Display { get; set; }

    // Changing this list takes a migration.
    public static readonly OrderCatalogItem[] Seeded =
    [
        Item("SYN-LAB-001", OrderType.Lab, "Complete blood count (CBC)"),
        Item("SYN-LAB-002", OrderType.Lab, "Basic metabolic panel (BMP)"),
        Item("SYN-LAB-003", OrderType.Lab, "Comprehensive metabolic panel (CMP)"),
        Item("SYN-LAB-004", OrderType.Lab, "Lipid panel"),
        Item("SYN-LAB-005", OrderType.Lab, "Hemoglobin A1c"),
        Item("SYN-LAB-006", OrderType.Lab, "Thyroid-stimulating hormone (TSH)"),
        Item("SYN-LAB-007", OrderType.Lab, "Urinalysis"),
        Item("SYN-LAB-008", OrderType.Lab, "Vitamin D, 25-hydroxy"),
        Item("SYN-IMG-001", OrderType.Imaging, "Chest X-ray, 2 views"),
        Item("SYN-IMG-002", OrderType.Imaging, "Lumbar spine X-ray"),
        Item("SYN-IMG-003", OrderType.Imaging, "Abdominal ultrasound"),
        Item("SYN-IMG-004", OrderType.Imaging, "CT head without contrast"),
        Item("SYN-IMG-005", OrderType.Imaging, "MRI lumbar spine without contrast"),
        Item("SYN-IMG-006", OrderType.Imaging, "Screening mammogram"),
        Item("SYN-MED-001", OrderType.Medication, "Lisinopril 10 mg tablet"),
        Item("SYN-MED-002", OrderType.Medication, "Metformin 500 mg tablet"),
        Item("SYN-MED-003", OrderType.Medication, "Atorvastatin 20 mg tablet"),
        Item("SYN-MED-004", OrderType.Medication, "Amlodipine 5 mg tablet"),
        Item("SYN-MED-005", OrderType.Medication, "Amoxicillin 500 mg capsule"),
        Item("SYN-MED-006", OrderType.Medication, "Albuterol 90 mcg inhaler"),
        Item("SYN-MED-007", OrderType.Medication, "Sertraline 50 mg tablet"),
        Item("SYN-MED-008", OrderType.Medication, "Ibuprofen 400 mg tablet"),
        Item("SYN-REF-001", OrderType.Referral, "Cardiology referral"),
        Item("SYN-REF-002", OrderType.Referral, "Dermatology referral"),
        Item("SYN-REF-003", OrderType.Referral, "Endocrinology referral"),
        Item("SYN-REF-004", OrderType.Referral, "Gastroenterology referral"),
        Item("SYN-REF-005", OrderType.Referral, "Orthopedics referral"),
        Item("SYN-REF-006", OrderType.Referral, "Physical therapy referral"),
        Item("SYN-REF-007", OrderType.Referral, "Psychiatry referral"),
    ];

    static OrderCatalogItem Item(string code, string type, string display) => new() { Code = code, Type = type, Display = display };
}
