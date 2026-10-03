using HRA.Provisioner.Model;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace HRA.Provisioner;

/// <summary>
/// Loads SYNTHETIC sample data: departments, designations, the Bengaluru office and 50 employees with a
/// manager hierarchy. All names, numbers and emails are made up (emails use example.com).
/// Idempotent: records are matched by code, name or work email and skipped if they exist.
/// Run after register-plugins so the plugins set owners and history.
/// </summary>
public sealed class Seeder
{
    private readonly IOrganizationService _service;
    private readonly Random _random = new(20260401);
    private readonly HashSet<string> _emails = new();

    public Seeder(IOrganizationService service) => _service = service;

    private static readonly (string Code, string Name)[] Departments =
    {
        ("EXE", "Executive Office"), ("HR", "Human Resources"), ("ENG", "Engineering"),
        ("FIN", "Finance"), ("SAL", "Sales"), ("OPS", "Operations"), ("ADM", "Administration"),
    };

    private static readonly (string Name, int Grade, string Department)[] Designations =
    {
        ("Chief Executive Officer", 7, "EXE"), ("Head of Department", 6, null), ("Manager", 5, null),
        ("Senior Software Engineer", 4, "ENG"), ("Software Engineer", 3, "ENG"), ("Associate Engineer", 2, "ENG"),
        ("HR Executive", 3, "HR"), ("Accountant", 3, "FIN"), ("Sales Executive", 3, "SAL"),
        ("Operations Executive", 3, "OPS"), ("Admin Executive", 2, "ADM"), ("Intern", 0, null),
    };

    private static readonly string[] FemaleNames = { "Aarti", "Ananya", "Deepa", "Divya", "Isha", "Kavya", "Lakshmi", "Meera", "Neha", "Pooja", "Priya", "Riya", "Sneha", "Swati", "Tanvi", "Vidya" };
    private static readonly string[] MaleNames = { "Aditya", "Arjun", "Karthik", "Manoj", "Nikhil", "Prakash", "Rahul", "Rajesh", "Rohan", "Sanjay", "Suresh", "Varun", "Vikram", "Vinay", "Yash", "Kiran" };
    private static readonly string[] LastNames = { "Rao", "Sharma", "Iyer", "Reddy", "Nair", "Kulkarni", "Hegde", "Shetty", "Menon", "Gowda", "Patil", "Joshi", "Bhat", "Kamath", "Naidu", "Pai" };

    public void Run()
    {
        Log.Step("Reference data");
        var departments = Departments.ToDictionary(d => d.Code, d => EnsureByKey("hra_department", "hra_code", d.Code, new Entity("hra_department")
        {
            ["hra_name"] = d.Name,
            ["hra_code"] = d.Code,
        }, d.Name));

        var designations = Designations.ToDictionary(d => d.Name, d =>
        {
            var record = new Entity("hra_designation")
            {
                ["hra_name"] = d.Name,
                ["hra_grade"] = new OptionSetValue(ChoiceDef.ValueOf(d.Grade)),
            };
            if (d.Department != null) record["hra_department"] = departments[d.Department];
            return EnsureByKey("hra_designation", "hra_name", d.Name, record, d.Name);
        });

        var bengaluru = EnsureByKey("hra_location", "hra_name", "Bengaluru - HQ", new Entity("hra_location")
        {
            ["hra_name"] = "Bengaluru - HQ",
            ["hra_city"] = "Bengaluru",
            ["hra_state"] = new OptionSetValue(ChoiceDef.ValueOf(Phase1.KarnatakaIndex)),
            ["hra_address"] = "1 Example Tech Park, Outer Ring Road, Bengaluru 560103",
            ["hra_weeklyoffpattern"] = new OptionSetValue(ChoiceDef.ValueOf(0)),
        }, "Bengaluru - HQ");

        Log.Step("Employees (synthetic)");
        EntityReference Add(string department, string designation, EntityReference manager, bool intern = false)
            => EnsureEmployee(departments[department], designations[designation], bengaluru, manager, intern);

        var ceo = Add("EXE", "Chief Executive Officer", null);
        var heads = Departments.Where(d => d.Code != "EXE").ToDictionary(d => d.Code, d => Add(d.Code, "Head of Department", ceo));

        var engManagers = new[] { Add("ENG", "Manager", heads["ENG"]), Add("ENG", "Manager", heads["ENG"]) };
        foreach (var manager in engManagers)
        {
            for (var i = 0; i < 8; i++)
            {
                Add("ENG", i < 2 ? "Senior Software Engineer" : i < 6 ? "Software Engineer" : "Associate Engineer", manager);
            }
        }
        for (var i = 0; i < 4; i++) Add("ENG", "Intern", engManagers[i % 2], intern: true);

        for (var i = 0; i < 3; i++) Add("HR", "HR Executive", heads["HR"]);
        for (var i = 0; i < 3; i++) Add("FIN", "Accountant", heads["FIN"]);
        var salesManager = Add("SAL", "Manager", heads["SAL"]);
        for (var i = 0; i < 6; i++) Add("SAL", "Sales Executive", salesManager);
        for (var i = 0; i < 5; i++) Add("OPS", "Operations Executive", heads["OPS"]);
        for (var i = 0; i < 3; i++) Add("ADM", "Admin Executive", heads["ADM"]);

        Log.Step("Department heads");
        foreach (var (code, head) in heads)
        {
            _service.Update(new Entity("hra_department", departments[code].Id) { ["hra_departmenthead"] = head });
        }
        _service.Update(new Entity("hra_department", departments["EXE"].Id) { ["hra_departmenthead"] = ceo });
        Log.Ok("Department heads set");

        LinkCurrentUser(heads["HR"]);
    }

    private EntityReference EnsureByKey(string table, string keyColumn, string keyValue, Entity record, string label)
    {
        var existing = _service.FindOne(table, new[] { keyColumn }, (keyColumn, keyValue));
        if (existing != null)
        {
            Log.Skip($"{label} exists");
            return existing.ToEntityReference();
        }
        var id = _service.Create(record);
        Log.Ok($"{label} created");
        return new EntityReference(table, id);
    }

    private EntityReference EnsureEmployee(EntityReference department, EntityReference designation, EntityReference location, EntityReference manager, bool intern)
    {
        // The random sequence is fixed, so the same 50 people are generated on every run.
        var female = _random.Next(2) == 0;
        var first = Pick(female ? FemaleNames : MaleNames);
        var last = Pick(LastNames);
        var email = $"{first}.{last}{_random.Next(10, 99)}@example.com".ToLowerInvariant();
        while (!_emails.Add(email)) email = $"{first}.{last}{_random.Next(100, 999)}@example.com".ToLowerInvariant();
        var joined = intern ? DateTime.Today.AddMonths(-_random.Next(1, 4)) : new DateTime(2015, 1, 1).AddDays(_random.Next(0, 4000));
        var born = new DateTime(1972, 1, 1).AddDays(_random.Next(0, 11000));
        var onProbation = (DateTime.Today - joined).TotalDays < 180;
        var grade = _random.Next(3, 30);

        var employee = new Entity("hra_employee")
        {
            ["hra_firstname"] = first,
            ["hra_lastname"] = last,
            ["hra_fullname"] = $"{first} {last}", // The plugin sets this too; kept in case plugins aren't registered yet.
            ["hra_workemail"] = email,
            ["hra_personalemail"] = $"{first}.{last}.home@example.com".ToLowerInvariant(),
            ["hra_mobile"] = $"+91 9{_random.Next(100000000, 999999999)}",
            ["hra_dateofbirth"] = born,
            ["hra_gender"] = new OptionSetValue(ChoiceDef.ValueOf(female ? 0 : 1)),
            ["hra_dateofjoining"] = joined,
            ["hra_employmenttype"] = new OptionSetValue(ChoiceDef.ValueOf(intern ? 3 : onProbation ? 1 : 0)),
            ["hra_employmentstatus"] = new OptionSetValue(ChoiceDef.ValueOf(0)),
            ["hra_department"] = department,
            ["hra_designation"] = designation,
            ["hra_location"] = location,
            ["hra_reportingmanager"] = manager,
            // Synthetic values in valid formats, to test column security. Not real people.
            ["hra_pan"] = $"{Letters(3)}P{Letters(1)}{_random.Next(1000, 9999)}{Letters(1)}",
            ["hra_aadhaarlast4"] = _random.Next(1000, 9999).ToString(),
            ["hra_uan"] = "10" + _random.Next(1000000, 9999999) + _random.Next(100, 999),
            ["hra_bankaccountnumber"] = "5010" + _random.Next(10000000, 99999999),
            ["hra_ifsc"] = "HDFC0" + _random.Next(100000, 999999),
            ["hra_bankname"] = "HDFC Bank",
            ["hra_annualctc"] = new Money(intern ? 240000 : grade * 100000),
        };
        if (!onProbation && !intern) employee["hra_confirmationdate"] = joined.AddMonths(6);
        var contact = _random.Next(3) == 0 ? BuildEmergencyContact(last) : null;

        // Every random value is drawn before this check, so re-runs generate the same people in the same order.
        var existing = _service.FindOne("hra_employee", new[] { "hra_employeeid" }, ("hra_workemail", email));
        if (existing != null)
        {
            Log.Skip($"{first} {last} exists");
            return existing.ToEntityReference();
        }

        var id = _service.Create(employee);
        if (contact != null)
        {
            contact["hra_employee"] = new EntityReference("hra_employee", id);
            _service.Create(contact);
        }
        Log.Ok($"{first} {last} ({email})");
        return new EntityReference("hra_employee", id);
    }

    private Entity BuildEmergencyContact(string lastName)
    {
        return new Entity("hra_emergencycontact")
        {
            ["hra_name"] = $"{Pick(_random.Next(2) == 0 ? FemaleNames : MaleNames)} {lastName}",
            ["hra_relationship"] = new OptionSetValue(ChoiceDef.ValueOf(_random.Next(0, 3))),
            ["hra_phone"] = $"+91 8{_random.Next(100000000, 999999999)}",
            ["hra_isprimary"] = true,
        };
    }

    /// <summary>Links the signed-in user to the Head of HR, so you can test as an HR manager with a team.</summary>
    private void LinkCurrentUser(EntityReference headOfHr)
    {
        var me = ((WhoAmIResponse)_service.Execute(new WhoAmIRequest())).UserId;
        if (_service.FindOne("hra_employee", new[] { "hra_employeeid" }, ("hra_systemuser", me)) != null)
        {
            Log.Skip("Your user is already linked to an employee");
            return;
        }
        _service.Update(new Entity("hra_employee", headOfHr.Id) { ["hra_systemuser"] = new EntityReference("systemuser", me) });
        Log.Ok("Your user is linked to the Head of Human Resources employee record");
    }

    private string Pick(string[] values) => values[_random.Next(values.Length)];

    private string Letters(int count) =>
        new(Enumerable.Range(0, count).Select(_ => (char)('A' + _random.Next(26))).ToArray());
}
