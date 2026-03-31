using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LotusLMSClient.Models;

namespace LotusLMSClient.Services
{
    public class CourseService
    {
        private readonly ApiService _apiService;
        private readonly AuthService _authService;
        private int _enrolmentPlanIid = 000000000;
        private const string StudentCoursesRi = "90e6affb162954a72ccb4917ad194919";
        private const string StudentCoursesRit = "3be8e77646c3da451a159ac537ce04df:ke7zTTwkVJf0SJeMDMEthbD60HcOUUTZRGZPN0bw2v4R0FhoeTvoh8rw5rvaYERv";

        public CourseService(ApiService apiService, AuthService authService = null)
        {
            _apiService = apiService;
            _authService = authService;
        }

        public async Task<List<DashboardCourseItem>> GetDashboardCoursesAsync()
        {
            if (_authService == null || !_authService.IsAuthenticated)
            {
                return new List<DashboardCourseItem>();
            }

            var enrolmentPlansResponse = await FetchStudentEnrolmentPlansResponseAsync();
            if (enrolmentPlansResponse == null || enrolmentPlansResponse.Result == null || enrolmentPlansResponse.Result.Count == 0)
            {
                return new List<DashboardCourseItem>();
            }

            _enrolmentPlanIid = enrolmentPlansResponse.Result[0].Iid;

            var response = await FetchStudentCoursesResponseAsync(_enrolmentPlanIid);
            if (response == null)
            {
                return new List<DashboardCourseItem>();
            }

            return MapDashboardCourses(response);
        }

        private async Task<StudentEnrolmentPlansResponse> FetchStudentEnrolmentPlansResponseAsync()
        {
            var endpoint = "/student/api/get-student-enrolment-plans?_sand_get_total=0&items_per_page=12&submit=1&page=1&_sand_ajax=1&_sand_platform=3&_sand_readmin=1&_sand_is_wan=false&_sand_ga_sessionToken=&_sand_ga_browserToken=&_sand_domain=truongson&_sand_masked=&_sand_session_id=2c710456-c150-42e1-85a6-f6e7ccbe7383&_sand_use_internal_network=0&allow_cache_api_cdn=1&_sand_user_agent=Mozilla/5.0%20(Windows%20NT%2010.0;%20Win64;%20x64)%20AppleWebKit/537.36%20(KHTML,%20like%20Gecko)%20Chrome/146.0.0.0%20Safari/537.36";
            var formData = new Dictionary<string, string>
            {
                { "_sand_token", _authService.CurrentToken },
                { "_sand_uiid", _authService.CurrentUserIid.ToString() },
                { "_sand_uid", _authService.CurrentUserId }
            };

            return await _apiService.PostFormDataAsync<StudentEnrolmentPlansResponse>(endpoint, formData);
        }

        private async Task<StudentCoursesResponse> FetchStudentCoursesResponseAsync(int enrolmentPlanIid)
        {
            var endpoint = $"/student/api/get-student-courses-in-enrolment-plan-table-view?_sand_get_total=0&items_per_page=20&enrolment_plan_iid={enrolmentPlanIid}&expand%5B%5D=syllabus&expand%5B%5D=score_by_rubric&submit=1&page=1&_sand_ajax=1&_sand_platform=3&_sand_readmin=1&_sand_is_wan=false&_sand_ga_sessionToken=&_sand_ga_browserToken=&_sand_domain=truongson&_sand_masked=&_sand_session_id={AuthService.SandSessionId}&_sand_use_internal_network=0&allow_cache_api_cdn=1&_sand_user_agent=Mozilla%2F5.0%20(Windows%20NT%2010.0%3B%20Win64%3B%20x64)%20AppleWebKit%2F537.36%20(KHTML%2C%20like%20Gecko)%20Chrome%2F146.0.0.0%20Safari%2F537.36";
            var formData = new Dictionary<string, string>
            {
                { "_sand_web_url", $"{ApiService.WEB_ORIGIN}/student/ep/{enrolmentPlanIid}" },
                { "_sand_device_uuid", AuthService.SandDeviceUuid },
                { "_sand_token", _authService.CurrentToken },
                { "_sand_uiid", _authService.CurrentUserIid.ToString() },
                { "_sand_uid", _authService.CurrentUserId },
                { "_sand_ri", StudentCoursesRi },
                { "_sand_rit", StudentCoursesRit }
            };

            var headers = new Dictionary<string, string>
            {
                { "Referer", $"{ApiService.WEB_ORIGIN}/student/ep/{enrolmentPlanIid}" }
            };

            return await _apiService.PostFormDataAsync<StudentCoursesResponse>(endpoint, formData, headers);
        }

        private static List<DashboardCourseItem> MapDashboardCourses(StudentCoursesResponse response)
        {
            if (response?.Result == null)
            {
                return new List<DashboardCourseItem>();
            }

            var courses = new List<DashboardCourseItem>();
            foreach (var item in response.Result)
            {
                if (item?.Course != null)
                {
                    var name = !string.IsNullOrWhiteSpace(item.Course.Name) ? item.Course.Name : item.Name;
                    courses.Add(new DashboardCourseItem
                    {
                        Name = name,
                        CpPercent = item.Course.Cp,
                        PHours = item.Course.P
                    });
                }

                if (item?.Children == null)
                {
                    continue;
                }

                foreach (var child in item.Children)
                {
                    if (child?.Course == null)
                    {
                        continue;
                    }

                    var childName = !string.IsNullOrWhiteSpace(child.Course.Name) ? child.Course.Name : child.Name;
                    courses.Add(new DashboardCourseItem
                    {
                        Name = childName,
                        CpPercent = child.Course.Cp,
                        PHours = child.Course.P
                    });
                }
            }

            return courses;
        }

        public async Task<List<Course>> GetUserCoursesAsync()
        {
            var dashboardCourses = await GetDashboardCoursesAsync();
            var courses = new List<Course>();
            var id = 1;

            foreach (var item in dashboardCourses)
            {
                courses.Add(new Course
                {
                    Id = id++,
                    Name = item.Name,
                    InstructorName = string.Empty,
                    CompletedLessons = 0,
                    TotalLessons = 0,
                    ProgressPercent = item.CpPercent,
                    Status = item.CpPercent >= 71 ? "Completed" : "Learning"
                });
            }

            return courses;
        }
    }
}
