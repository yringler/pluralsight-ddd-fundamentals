﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorShared.Models.AppointmentType;
using BlazorShared.Models.Room;
using FrontDesk.Core.Aggregates;
using FrontDesk.Core.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PluralsightDdd.SharedKernel;

namespace FrontDesk.Infrastructure.Data
{
  public class AppDbContextSeed
  {
    private Doctor DrSmith => new Doctor(1, "Dr. Smith");
    private Doctor DrWho => new Doctor(2, "Dr. Who");
    private Doctor DrMcDreamy => new Doctor(3, "Dr. McDreamy");
    private readonly Guid _scheduleId = Guid.Parse("f9369039-9d11-4442-9738-ed65d8a8ad52");
    private DateTime _testDate = DateTime.Now;
    public const string MALE_SEX = "Male";
    public const string FEMALE_SEX = "Female";
    private readonly AppDbContext _context;
    private readonly ILogger<AppDbContextSeed> _logger;
    private Client _steve;
    private Client _julie;
    private Patient _darwin;
    private Patient _sampson;

    public AppDbContextSeed(AppDbContext context,
      ILogger<AppDbContextSeed> logger)
    {
      _context = context;
      _logger = logger;
    }

    public async Task SeedAsync(DateTime testDate, int? retry = 0)
    {
      _logger.LogInformation($"Seeding data.");
      _logger.LogInformation($"DbContext Type: {_context.Database.ProviderName}");

      _testDate = testDate;
      int retryForAvailability = retry.Value;
      try
      {
        if(_context.IsRealDatabase())
        {
          // apply migrations if connecting to a SQL database
          _context.Database.Migrate();
        }

        if (!await _context.Schedules.AnyAsync())
        {
          await _context.Schedules.AddAsync(
              CreateSchedule());

          await _context.SaveChangesAsync();
        }

        if (!await _context.AppointmentTypes.AnyAsync())
        {
          var apptTypes = await CreateAppointmentTypes();
          await _context.AppointmentTypes.AddRangeAsync(apptTypes);
          await _context.SaveChangesWithIdentityInsert<AppointmentType>();
        }

        if (!await _context.Doctors.AnyAsync())
        {
          var doctors = CreateDoctors();
          await _context.Doctors.AddRangeAsync(doctors);
          await _context.SaveChangesWithIdentityInsert<Doctor>();
        }

        if (!await _context.Clients.AnyAsync())
        {
          await _context.Clients.AddRangeAsync(
              CreateListOfClientsWithPatients(DrSmith, DrWho, DrMcDreamy));

          await _context.SaveChangesAsync();
        }

        if (!await _context.Rooms.AnyAsync())
        {
          var rooms = await CreateRooms();
          await _context.Rooms.AddRangeAsync(rooms);
          await _context.SaveChangesWithIdentityInsert<Room>();
        }

        if (!await _context.Appointments.AnyAsync())
        {
          _steve = _context.Clients.FirstOrDefault(c => c.FullName == "Steve Smith");
          _julie = _context.Clients.FirstOrDefault(c => c.FullName == "Julia Lerman");
          _darwin = _context.Patients.FirstOrDefault(p => p.Name == "Darwin");
          _sampson = _context.Patients.FirstOrDefault(p => p.Name == "Sampson");
          var rooms = _context.Rooms.ToList();
          var apptTypes = _context.AppointmentTypes.ToList();
          await _context.Appointments.AddRangeAsync(
              CreateAppointments(_scheduleId));

          await _context.SaveChangesAsync();
        }
      }
      catch (Exception ex)
      {
        if (retryForAvailability < 1)
        {
          retryForAvailability++;
          _logger.LogError(ex.Message);
          await SeedAsync(_testDate, retryForAvailability);
        }
        throw;
      }

      await _context.SaveChangesAsync();
    }

    private async Task<List<Room>> CreateRooms()
    {
      string fileName = "rooms.json";
      if (!File.Exists(fileName))
      {
        _logger.LogInformation($"Creating {fileName}");
        using Stream writer = new FileStream(fileName, FileMode.OpenOrCreate);
        await JsonSerializer.SerializeAsync(writer, GetDefaultRooms());
      }

      _logger.LogInformation($"Reading rooms from file {fileName}");
      using Stream reader = new FileStream(fileName, FileMode.Open);
      var rooms = await JsonSerializer.DeserializeAsync<List<RoomDto>>(reader);

      return rooms.Select(dto => new Room(dto.RoomId, dto.Name)).ToList();
    }

    private List<RoomDto> GetDefaultRooms()
    {
      List<RoomDto> rooms = new List<RoomDto>();
      for (int i = 1; i < 6; i++)
      {
        rooms.Add(new RoomDto { RoomId = i, Name = $"Exam Room {i}" });
      }
      return rooms;
    }

    private Schedule CreateSchedule()
    {
      return new Schedule(_scheduleId, new DateTimeRange(_testDate, _testDate), 1, null);
    }

    private async Task<List<AppointmentType>> CreateAppointmentTypes()
    {
      string fileName = "appointmentTypes.json";
      if (!File.Exists(fileName))
      {
        _logger.LogInformation($"Creating {fileName}");
        using Stream writer = new FileStream(fileName, FileMode.OpenOrCreate);
        await JsonSerializer.SerializeAsync(writer, GetDefaultAppointmentTypes());
      }

      _logger.LogInformation($"Reading appointment types from file {fileName}");
      using Stream reader = new FileStream(fileName, FileMode.Open);
      var apptTypes = await JsonSerializer.DeserializeAsync<List<AppointmentTypeDto>>(reader);

      return apptTypes.Select(dto => new AppointmentType(dto.AppointmentTypeId, dto.Name, dto.Code, dto.Duration)).ToList();
    }

    private List<AppointmentTypeDto> GetDefaultAppointmentTypes()
    {
      var result = new List<AppointmentTypeDto>
            {
                new AppointmentTypeDto {
                  AppointmentTypeId=1,
                  Name="Wellness Exam",
                  Code="WE",
                  Duration=30
                },
                new AppointmentTypeDto {
                  AppointmentTypeId=2,
                  Name="Diagnostic Exam",
                  Code="DE",
                  Duration=60
                },
                new AppointmentTypeDto{
                  AppointmentTypeId=3,
                  Name="Nail Trim",
                  Code="NT",
                  Duration=30
                }
            };

      return result;
    }

    private List<Doctor> CreateDoctors()
    {
      var result = new List<Doctor>
            {
                DrSmith,
                DrWho,
                DrMcDreamy
            };

      return result;
    }

    private IEnumerable<Client> CreateListOfClientsWithPatients(Doctor drSmith, Doctor drWho, Doctor drMcDreamy)
    {
      var clientGraphs = new List<Client>();

      var clientSmith = (CreateClientWithPatient("Steve Smith", "Steve", "Mr.", drSmith.Id, MALE_SEX, "Darwin", "Dog",
        "Poodle"));
      clientSmith.Patients.Add(new Patient(1, "Arya", FEMALE_SEX, new AnimalType("Cat", "Feral"), drWho.Id));
      clientSmith.Patients.Add(new Patient(1, "Rosie", FEMALE_SEX, new AnimalType("Dog", "Golden Retriever"), drWho.Id));

      clientGraphs.Add(clientSmith);

      clientGraphs.Add(CreateClientWithPatient("Julia Lerman", "Julie", "Mrs.", drMcDreamy.Id, MALE_SEX, "Sampson", "Dog", "Newfoundland"));



      return clientGraphs;
    }

    private static Client CreateClientWithPatient(string fullName,
        string preferredName,
        string salutation,
        int doctorId,
        string patient1Sex,
        string patient1Name,
        string animalType,
        string breed)
    {
      var client = new Client(fullName, preferredName, salutation, doctorId, "client@example.com");
      client.Patients.Add(new Patient(1, patient1Name, patient1Sex, new AnimalType(animalType, breed), doctorId));

      return client;
    }

    private IEnumerable<Appointment> CreateAppointments(Guid scheduleId)
    {
      int wellnessVisit = 1;
      int diagnosticVisit = 2;
      int room1 = 1;
      int room2 = 2;
      int room3 = 3;
      var appointmentList = new List<Appointment>
              {
                new Appointment(
                    wellnessVisit,
                    scheduleId,
                    _steve.Id,
                    DrSmith.Id,
                    _darwin.Id,
                    room1,
                    new DateTimeRange(_testDate.AddHours(10), TimeSpan.FromMinutes(30)),
                    "(WE) Darwin - Steve Smith"),
                new Appointment(
                    wellnessVisit,
                    scheduleId,
                    _steve.Id,
                    DrSmith.Id,
                    _steve.Patients[1].Id,
                    room1,
                    new DateTimeRange(_testDate.AddHours(10).AddMinutes(30), TimeSpan.FromMinutes(30)),
                    "(WE) Arya - Steve Smith"),
                new Appointment(
                    wellnessVisit,
                    scheduleId,
                    _steve.Id,
                    DrSmith.Id,
                    _steve.Patients[2].Id,
                    room1,
                    new DateTimeRange(_testDate.AddHours(11), TimeSpan.FromMinutes(30)),
                    "(WE) Rosie - Steve Smith"),
                new Appointment(
                    diagnosticVisit,
                    scheduleId,
                    _julie.Id,
                    DrWho.Id,
                    _sampson.Id,
                    room2,
                    new DateTimeRange(_testDate.AddHours(11), TimeSpan.FromMinutes(60)),
                    "(DE) Sampson - Julie Lerman")
              };

      return appointmentList;
    }
  }
}
