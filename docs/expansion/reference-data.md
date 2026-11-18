# Reference data

The standard code lists SwiftChart loads, where each comes from, and what its publisher asks of anyone who uses it. Nothing is fetched while the apps run: the codes are in the repository as generated SQL, so the clinic's server works with no internet (NFR-08).

## How it is loaded

1. Download the pinned source files into a folder outside the repository. The addresses, file names and SHA-256 checksums are at the top of `tools/reference/import.cs`.
2. Run `dotnet run tools/reference/import.cs -- --sources <folder>`. It checks each file against its checksum, reads it, and writes `db/reference/10-clinical-codes.sql`.
3. Commit the SQL. The migration job (`infra/migrate`), the tests' database and the benchmark apply `db/reference/*.sql` after `db/policies`, as `ehr_owner`. The file is safe to run again: it leaves `ehr.clinical_codes` holding exactly its rows.

To move to a newer release of a source, change its address, the file's name inside the archive and its checksum in the script, download it, and run the script again.

The table is `ehr.clinical_codes`: not patient data, read by both apps, and changed by nothing at runtime.

## What is loaded today

A subset, chosen in the script: the diagnoses, medications and allergens a primary-care clinic meets most. The plan's step 2a widens it to the full lists.

| Used for | Source and release | What is taken | Rows |
| --- | --- | --- | --- |
| Diagnoses | ICD-10-CM, fiscal year 2027 (CDC, National Center for Health Statistics) | The codes valid on a claim under 44 three-character categories, with their long descriptions | 821 |
| Medications | RxNorm Current Prescribable Content, 2 November 2026 (U.S. National Library of Medicine) | Clinical drugs (term type SCD) of one ingredient, for 33 ingredients | 316 |
| Drug allergens | The same RxNorm release | Ingredients (term type IN), 19 of them | 19 |
| Other allergens (foods, latex, venom, pollen and the like) | FDA Unique Ingredient Identifiers (UNII), 4 August 2026 (U.S. Food and Drug Administration) | 21 substances, by their preferred names, each given a FHIR allergy category by this project | 21 |

Names are the sources' own. Two changes of form only: an ICD-10-CM code is written with its dot (`E11.9`), and a UNII or RxNorm ingredient name is written in sentence case ("Natural latex rubber").

## Licenses and attribution

- **RxNorm.** The Current Prescribable Content needs no license. This product uses publicly available data courtesy of the U.S. National Library of Medicine (NLM), National Institutes of Health, Department of Health and Human Services; NLM is not responsible for the product and does not endorse or recommend this or any other product. The data here is the release named above and may not be current. The full RxNorm release, which needs a UMLS license, is not used.
- **ICD-10-CM.** Published by the U.S. government (CDC/NCHS) for public use.
- **UNII.** Published by the U.S. Food and Drug Administration. No license is stated on its download page; as a work of the U.S. government it is taken to be in the public domain.
- **FHIR value sets.** The status, category, criticality and severity codes are HL7 FHIR R4's, which HL7 publishes under Creative Commons "No Rights Reserved" (CC0).

## Not loaded, and why

- **SNOMED CT and CPT**: both need licenses. Neither is used anywhere; "No known allergies" is recorded with the app's own marker.
- **LOINC, MED-RT (drug classes), UCUM, CVX, PHQ-9 and GAD-7, and a drug-interaction list**: these belong to later steps of `docs/expansion/PLAN.md`. LOINC's download needs a free account, MED-RT's terms are unclear, and no interaction list with a clearly free license has been found, so each waits for Tahseen's decision.

## Which codes are sensitive

A diagnosis is sensitive when its ICD-10-CM code is in one of these ranges, or when a clinician marks it:

| Range | What it covers |
| --- | --- |
| F01–F99 | Mental, behavioral and neurodevelopmental disorders, including substance use (F10–F19) |
| A50–A64 | Infections with a predominantly sexual mode of transmission |
| B20, Z21 | HIV disease, and asymptomatic HIV infection status |
| O04, Z33.2 | Termination of pregnancy |
| T74, T76 | Abuse, neglect and other maltreatment, confirmed or suspected |
| X71–X83, R45.851 | Intentional self-harm, and suicidal ideations |

The rule is `ehr.icd10cm_is_sensitive` in `db/policies/65-chart-lists.sql`; changing it is a reviewed change to that file. A medication is sensitive when a clinician marks it.
