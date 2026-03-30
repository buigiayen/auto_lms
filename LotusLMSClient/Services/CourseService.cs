using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LotusLMSClient.Models;

namespace LotusLMSClient.Services
{
    public class CourseService
    {
        private readonly ApiService _apiService;

        public CourseService(ApiService apiService)
        {
            _apiService = apiService;
        }

        public async Task<List<Course>> GetUserCoursesAsync()
        {
            // In a real app, we would fetch from _apiService
            // Demo data for UI presentation
            await Task.Delay(500); // Simulate network latency

            var driverLessons = new List<Lesson>
            {
                new Lesson { Id = 1, Title = "Khái niệm và quy tắc giao thông", ContentType = "Video", IsCompleted = true },
                new Lesson { Id = 2, Title = "Hệ thống biển báo hiệu đường bộ", ContentType = "Video", IsCompleted = true },
                new Lesson { Id = 3, Title = "Giải các thế sa hình", ContentType = "Video", IsCompleted = false },
                new Lesson { Id = 4, Title = "Cấu tạo và sửa chữa thông thường", ContentType = "PDF", IsCompleted = false },
                new Lesson { Id = 5, Title = "Đạo đức người lái xe", ContentType = "Html", IsCompleted = false }
            };

            var mechanicLessons = new List<Lesson>
            {
                new Lesson { Id = 101, Title = "Động cơ đốt trong cơ bản", ContentType = "Video", IsCompleted = true },
                new Lesson { Id = 102, Title = "Hệ thống điện xe ô tô", ContentType = "Video", IsCompleted = true },
                new Lesson { Id = 103, Title = "Hệ thống truyền lực", ContentType = "PDF", IsCompleted = true }
            };

            var courses = new List<Course>
            {
                new Course
                {
                    Id = 1,
                    Name = "Lý thuyết GPLX Hạng B2",
                    InstructorName = "Nguyễn Văn A",
                    TotalLessons = driverLessons.Count,
                    CompletedLessons = 2,
                    ProgressPercent = 40,
                    Status = "Learning",
                    Lessons = driverLessons
                },
                new Course
                {
                    Id = 2,
                    Name = "An toàn giao thông",
                    InstructorName = "Trần Văn B",
                    TotalLessons = 10,
                    CompletedLessons = 8,
                    ProgressPercent = 80,
                    Status = "Learning",
                    Lessons = new List<Lesson>()
                },
                new Course
                {
                    Id = 3,
                    Name = "Sửa chữa ôtô cơ bản",
                    InstructorName = "Lê Thị C",
                    TotalLessons = mechanicLessons.Count,
                    CompletedLessons = 3,
                    ProgressPercent = 100,
                    Status = "Completed",
                    Lessons = mechanicLessons
                }
            };
            return courses;
        }
    }
}
