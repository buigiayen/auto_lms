using LotusLMSClient.Helpers;
using LotusLMSClient.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace LotusLMSClient.Services
{
    public class CourseService
    {
        private readonly ApiService _apiService;
        private readonly AuthService _authService;
        
        public AuthService AuthService => _authService;

        private int _enrolmentPlanIid = 000000000;
        private const string StudentCoursesRi = "8cbcfef31fd3b4c730a90124640619ad";
        private const string StudentCoursesRit = "ddfd1e7d986bb7743a479117dc72a574:1ebBJRNcQOy0/fovqo+ofL9ulu1gaNqOhkaK+L9TCUoVtMWHmFxib42uF9K1ymlb";

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

         public async Task<bool> ReportLearningTimeAsync(int courseIid)
        {
            if (_authService == null || !_authService.IsAuthenticated) return false;

            var endpoint = $"/student/api/get-detail-learning-time?enrolment_plan_iid={_enrolmentPlanIid}&course_iid={courseIid}&expand%5B%5D=current_unit&expand%5B%5D=current_unit_group&submit=1&_sand_ajax=1&_sand_platform=3&_sand_readmin=1&_sand_is_wan=false&_sand_ga_sessionToken=&_sand_ga_browserToken=&_sand_domain=truongson&_sand_masked=&_sand_session_id={AuthService.SandSessionId}&_sand_use_internal_network=0&allow_cache_api_cdn=1&_sand_user_agent=Mozilla%2F5.0%20(Windows%20NT%2010.0%3B%20Win64%3B%20x64)%20AppleWebKit%2F537.36%20(KHTML%2C%20like%20Gecko)%20Chrome%2F146.0.0.0%20Safari%2F537.36";

            var formData = new Dictionary<string, string>
            {
                { "_sand_web_url", $"{ApiService.WEB_ORIGIN}/student/courses/{courseIid}" },
                { "_sand_device_uuid", AuthService.SandDeviceUuid },
                { "_sand_token", _authService.CurrentToken },
                { "_sand_uiid", _authService.CurrentUserIid.ToString() },
                { "_sand_uid", _authService.CurrentUserId }
            };

            var headers = new Dictionary<string, string>
            {
                { "Referer", $"{ApiService.WEB_ORIGIN}/student/courses/{courseIid}" }
            };

            try 
            {
                var response = await _apiService.PostFormDataAsync<object>(endpoint, formData, headers);
                return response != null;
            }
            catch
            {
                return false;
            }
        }

        public async Task<SubjectDetails> GetDetailLearningTimeAsync(int courseIid)
        {
            if (_authService == null || !_authService.IsAuthenticated) return null;

            var endpoint = $"/time-tracking/course-time-tracking/get-detail-learning-time?user_iid={_authService.CurrentUserIid}&course_iid={courseIid}&item_ntype=video&_sand_session_id=2fc65b08-b381-461c-ad63-80f06fdeaa23&_sand_user_agent=Mozilla/5.0%20(Windows%20NT%2010.0;%20Win64;%20x64)%20AppleWebKit/537.36%20(KHTML,%20like%20Gecko)%20Chrome/146.0.0.0%20Safari/537.36";

            var epUrl = $"{ApiService.WEB_ORIGIN}/student/course/{courseIid}"; // From curl
            var formData = new Dictionary<string, string>
            {
                { "_sand_web_url", epUrl },
                { "_sand_device_uuid", AuthService.SandDeviceUuid },
                { "_sand_token", _authService.CurrentToken },
                { "_sand_uiid", _authService.CurrentUserIid.ToString() },
                { "_sand_uid", _authService.CurrentUserId },
                { "_sand_ri", StudentCoursesRi },
                { "_sand_rit", StudentCoursesRit }
            };

            var headers = new Dictionary<string, string> { { "Referer", $"{ApiService.WEB_ORIGIN}/student/course/{courseIid}" } };

            try
            {
                var response = await _apiService.PostFormDataAsync<SubjectDetails>(endpoint, formData, headers);
                return response;
            }
            catch { return null; }
        }

        public async Task<bool> SaveItemLearningTimeAsync(int itemIid, string itemNtype, int courseIid, int timeSpent)
        {
            if (_authService == null || !_authService.IsAuthenticated) return false;

            var endpoint = "/course/api/save-item-learning-time";
            var requestUrl = $"{ApiService.WEB_ORIGIN}/learn/course/learn/{courseIid}/{itemIid}";

            var formData = new Dictionary<string, string>
            {
                { "item_iid", itemIid.ToString() },
                { "item_ntype", itemNtype },
                { "course_iid", courseIid.ToString() },
                { "time_spent", timeSpent.ToString() },
                { "_sand_domain", "truongson" },
                { "_sand_web_url", requestUrl },
                { "_sand_device_uuid", AuthService.SandDeviceUuid },
                { "_sand_session_id", AuthService.SandSessionId },
                { "_sand_token", _authService.CurrentToken },
                { "_sand_uiid", _authService.CurrentUserIid.ToString() },
                { "_sand_uid", _authService.CurrentUserId },
                { "_sand_user_agent", AuthService.SandUserAgent },
                { "_sand_ri", StudentCoursesRi },
                { "_sand_rit", StudentCoursesRit }
            };

            var headers = new Dictionary<string, string> 
            { 
                { "Referer", requestUrl },
                { "Origin", ApiService.WEB_ORIGIN }
            };

            try
            {
                var response = await _apiService.PostFormDataAsync<object>(endpoint, formData, headers);
                return response != null;
            }
            catch { return false; }
        }

        public async Task<bool> TrackTimeAsync(int courseIid, double timeSpent = 1.0)
        {
            if (_authService == null || !_authService.IsAuthenticated) return false;

            var endpoint = "/time-tracking/api/track-time";

            var request = new HttpRequestMessage(HttpMethod.Post, "https://staging-api.lotuslms.com/time-tracking/api/track-time");

            Dictionary<string, string> headers = new Dictionary<string, string>();
            foreach (var header in request.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }


            var formData = new Dictionary<string, string>
            {
                { "page_id", "learn"},
                { "item_id", courseIid.ToString() },
                { "time_spent", timeSpent.ToString("F3") },
                { "_sand_web_url", $"{ApiService.WEB_ORIGIN}/student" },
                { "_sand_device_uuid", AuthService.SandDeviceUuid },
                { "_sand_session_id", AuthService.SandSessionId },
                { "_sand_token", _authService.CurrentToken },
                { "_sand_uiid", _authService.CurrentUserIid.ToString() },
                { "_sand_uid", _authService.CurrentUserId },
                { "_sand_user_agent", AuthService.SandUserAgent },
                { "_sand_ri", StudentCoursesRi },
                { "_sand_rit", StudentCoursesRit }
            };
            try
            {
                var response = await _apiService.PostFormDataAsync<object>(endpoint, formData, headers);
                return response != null;

            }
            catch { return false; }
        }

        public async Task<KpiData> CheckTodayKpiTimeAsync(int courseIid)
        {
            if (_authService == null || !_authService.IsAuthenticated) return KpiData.Empty;

            var endpoint = $"/time-tracking/learning-time-tracking/today-gained-kpi-time?user_iid={_authService.CurrentUserIid}&submit=1&_sand_ajax=1&_sand_platform=3&_sand_readmin=1&_sand_is_wan=false&_sand_ga_sessionToken=&_sand_ga_browserToken=&_sand_domain=truongson&_sand_masked=&_sand_session_id={AuthService.SandSessionId}&_sand_use_internal_network=0&allow_cache_api_cdn=1&_sand_user_agent={Uri.EscapeDataString(AuthService.SandUserAgent)}";

            var requestUrl = $"{ApiService.WEB_ORIGIN}/learn/course/learn/{courseIid}";

            var formData = new Dictionary<string, string>
            {
                { "_sand_web_url", requestUrl },
                { "_sand_device_uuid", AuthService.SandDeviceUuid },
                { "_sand_token", _authService.CurrentToken },
                { "_sand_uiid", _authService.CurrentUserIid.ToString() },
                { "_sand_uid", _authService.CurrentUserId },
                { "_sand_ri", StudentCoursesRi },
                { "_sand_rit", StudentCoursesRit }
            };

            var headers = new Dictionary<string, string>
            {
                { "Referer", requestUrl },
                { "Origin", ApiService.WEB_ORIGIN }
            };

            try
            {
                var response = await _apiService.PostFormDataAsync<JObject>(endpoint, formData, headers);
                if (response == null) return KpiData.Empty;

                AppLog.Info($"KPI Check Response: {response.ToString(Newtonsoft.Json.Formatting.None)}", 0, _authService.CurrentUserIid);
                
                var result = response["result"];
                var data = KpiData.Empty;

                if (result != null)
                {
                    double gained = 0;
                    double required = 0;

                    var kpiSub = result["today_gained_kpi_time"];
                    if (kpiSub != null)
                    {
                        if (kpiSub["total_gained_kpi_time_in_seconds"] != null) double.TryParse(kpiSub["total_gained_kpi_time_in_seconds"].ToString(), out gained);
                        else if (kpiSub["gained_time"] != null) double.TryParse(kpiSub["gained_time"].ToString(), out gained);

                        if (kpiSub["max_gained_kpi_time_in_seconds"] != null) double.TryParse(kpiSub["max_gained_kpi_time_in_seconds"].ToString(), out required);
                        else if (kpiSub["required_time"] != null) double.TryParse(kpiSub["required_time"].ToString(), out required);
                    }

                    if (gained <= 0)
                    {
                        if (result["gained_time"] != null) double.TryParse(result["gained_time"].ToString(), out gained);
                        else if (result["today_gained_time"] != null) double.TryParse(result["today_gained_time"].ToString(), out gained);
                        else if (result["total_gained_time"] != null) double.TryParse(result["total_gained_time"].ToString(), out gained);
                    }
                    
                    if (required <= 0)
                    {
                        if (result["required_time"] != null) double.TryParse(result["required_time"].ToString(), out required);
                        else if (result["total_kpi_time"] != null) double.TryParse(result["total_kpi_time"].ToString(), out required);
                        else if (result["kpi_time"] != null) double.TryParse(result["kpi_time"].ToString(), out required);
                        else if (result["limit_time"] != null) double.TryParse(result["limit_time"].ToString(), out required);
                        else if (result["standard_time"] != null) double.TryParse(result["standard_time"].ToString(), out required);
                    }

                    data.GainedTime = gained;
                    data.RequiredTime = required;

                    if (result["percent"] != null && double.TryParse(result["percent"].ToString(), out double percent))
                    {
                        data.Percent = percent;
                    }
                    else if (required > 0)
                    {
                        data.Percent = (gained / required) * 100.0;
                    }

                    if (result["is_reached_kpi"] != null && result["is_reached_kpi"].Type == JTokenType.Boolean)
                    {
                        data.IsReached = (bool)result["is_reached_kpi"];
                    }
                    else if (kpiSub != null && kpiSub["max_online_learning_time_reached"] != null && kpiSub["max_online_learning_time_reached"].Type == JTokenType.Boolean)
                    {
                        data.IsReached = (bool)kpiSub["max_online_learning_time_reached"];
                    }
                    else if (required > 0)
                    {
                        data.IsReached = gained >= required;
                    }

                    if (required > 0)
                    {
                        double gainedHours = gained / 3600.0;
                        double requiredHours = required / 3600.0;
                        
                        if (gainedHours % 1 == 0 && requiredHours % 1 == 0)
                            data.FormattedProgress = $"{(int)gainedHours}/{(int)requiredHours} giờ";
                        else
                            data.FormattedProgress = $"{gainedHours:F2}/{requiredHours:F1} giờ";
                    }
                    else if (data.Percent > 0)
                    {
                        data.FormattedProgress = $"{data.Percent:F1}% (Chưa rõ giới hạn)";
                    }
                    else
                    {
                        data.FormattedProgress = "0/12 giờ (Đang cập nhật)";
                    }
                }

                return data;
            }
            catch (Exception ex)
            {
                AppLog.Error($"Lỗi check KPI: {ex.Message}", 0, _authService.CurrentUserIid);
                return KpiData.Empty;
            }
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
                        Iid = item.Course.Iid,
                        Name = name,
                        CpPercent = item.Course.Cp,
                        PHours = item.Course.P,
                        pf = item.Course.PF,
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
                    int.TryParse(child.Iid, out int childIid);
                    courses.Add(new DashboardCourseItem
                    {
                        Iid = child.Course.Iid,
                        Name = childName,
                        CpPercent = child.Course.Cp,
                        PHours = child.Course.P,
                        pf = child.Course.PF,
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
                    Iid = item.Iid,
                    Name = item.Name,
                    InstructorName = string.Empty,
                    CompletedLessons = 0,
                    TotalLessons = 0,
                    ProgressPercent = item.CpPercent,
                    Status = item.pf ==1 ? "Completed" : "Learning"
                });
            }

            return courses;
        }

        public static List<LearningTreeNode> ParseLearningHierarchy(JToken token)
        {
            var nodes = new List<LearningTreeNode>();

            if (token is JObject obj)
            {
                var nameToken = obj["name"];
                bool hasName = nameToken != null && !string.IsNullOrWhiteSpace(nameToken.ToString());

                if (hasName)
                {
                    var node = new LearningTreeNode { Name = nameToken.ToString() };

                    // Duyệt các property còn lại để tìm node con
                    foreach (var prop in obj.Properties())
                    {
                        if (prop.Name == "name") continue;
                        if (prop.Value is JObject || prop.Value is JArray)
                        {
                            node.Children.AddRange(ParseLearningHierarchy(prop.Value));
                        }
                    }

                    nodes.Add(node);
                }
                else
                {
                    // Không có name ở tầng này, tiếp tục duyệt sâu hơn
                    foreach (var prop in obj.Properties())
                    {
                        if (prop.Value is JObject || prop.Value is JArray)
                        {
                            nodes.AddRange(ParseLearningHierarchy(prop.Value));
                        }
                    }
                }
            }
            else if (token is JArray arr)
            {
                foreach (var item in arr)
                {
                    nodes.AddRange(ParseLearningHierarchy(item));
                }
            }

            return nodes;
        }
        public async Task<RubricResult?> GetExamCourseDetailAsync(int courseIid)
        {
            if (_authService == null || !_authService.IsAuthenticated) return null;

            var endpoint = $"/course/api/get-course?ntype=course&depth=1&editing_syllabus=1&ciid={courseIid}&_sand_expand%5B%5D=score_by_rubric&iid={courseIid}&submit=1&_sand_ajax=1&_sand_platform=3&_sand_readmin=1&_sand_is_wan=false&_sand_ga_sessionToken=&_sand_ga_browserToken=&_sand_domain=truongson&_sand_masked=&_sand_session_id={AuthService.SandSessionId}&_sand_use_internal_network=0&allow_cache_api_cdn=1&_sand_user_agent={Uri.EscapeDataString(AuthService.SandUserAgent)}";

            var webUrl = $"{ApiService.WEB_ORIGIN}/student/course/{courseIid}/dashboard";
            var formData = new Dictionary<string, string>
            {
                { "_sand_web_url", webUrl },
                { "_sand_device_uuid", AuthService.SandDeviceUuid },
                { "_sand_token", _authService.CurrentToken },
                { "_sand_uiid", _authService.CurrentUserIid.ToString() },
                { "_sand_uid", _authService.CurrentUserId },
                { "_sand_ri", StudentCoursesRi },
                { "_sand_rit", StudentCoursesRit }
            };

            var headers = new Dictionary<string, string>
            {
                { "Referer", webUrl },
                { "Origin", ApiService.WEB_ORIGIN }
            };

            try
            {
                var response = await _apiService.PostFormDataAsync<RubricResponse>(endpoint, formData, headers);
                return response?.Result;
            }
            catch (Exception ex)
            {
                AppLog.Error($"Lỗi lấy chi tiết khóa học: {ex.Message}", 0, _authService.CurrentUserIid);
                return null;
            }
        }
        public async Task<JObject> GetPracticeDetailAsync(int practiceIid, int courseIid)
        {
            if (_authService == null || !_authService.IsAuthenticated) return null;

            var endpoint = $"/practice/api/get-practice-detail?practice_iid={practiceIid}&course_iid={courseIid}&_sand_expand%5B%5D=exam_sco&submit=1&_sand_ajax=1&_sand_platform=3&_sand_readmin=1&_sand_is_wan=false&_sand_ga_sessionToken=&_sand_ga_browserToken=&_sand_domain=truongson&_sand_masked=&_sand_session_id={AuthService.SandSessionId}&_sand_use_internal_network=0&allow_cache_api_cdn=1&_sand_user_agent={Uri.EscapeDataString(AuthService.SandUserAgent)}";

            var requestUrl = $"{ApiService.WEB_ORIGIN}/learn/course/learn/{courseIid}";

            var formData = new Dictionary<string, string>
            {
                { "_sand_web_url", requestUrl },
                { "_sand_device_uuid", AuthService.SandDeviceUuid },
                { "_sand_token", _authService.CurrentToken },
                { "_sand_uiid", _authService.CurrentUserIid.ToString() },
                { "_sand_uid", _authService.CurrentUserId },
                { "_sand_ri", StudentCoursesRi },
                { "_sand_rit", StudentCoursesRit }
            };

            var headers = new Dictionary<string, string>
            {
                { "Referer", requestUrl },
                { "Origin", ApiService.WEB_ORIGIN }
            };

            try
            {
                var response = await _apiService.PostFormDataAsync<JObject>(endpoint, formData, headers);
                return response;
            }
            catch (Exception ex)
            {
                AppLog.Error($"Lỗi lấy chi tiết ôn luyện: {ex.Message}", 0, _authService.CurrentUserIid);
                return null;
            }
        }
        
        public async Task<JObject> GetQuestionBankStatusAsync(int examTemplateIid, int bankIid, int courseIid)
        {
            if (_authService == null || !_authService.IsAuthenticated) return null;

            // Construct the EXACT URL format provided by the user
            string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36";
            string sessionId = AuthService.SandSessionId;
            
            var endpoint = $"/practice/api/get-question-bank-status?" +
                           $"bank_iids[]={bankIid}&" +
                           $"exam_template_iid={examTemplateIid}&" +
                           $"last_ts=0&submit=1&_sand_ajax=1&_sand_platform=3&_sand_readmin=1&_sand_is_wan=false&" +
                           $"_sand_ga_sessionToken=&_sand_ga_browserToken=&" +
                           $"_sand_domain=truongson&_sand_masked=&" +
                           $"_sand_session_id={sessionId}&" +
                           $"_sand_use_internal_network=0&allow_cache_api_cdn=1&" +
                           $"_sand_user_agent={Uri.EscapeDataString(userAgent)}";

            var requestUrl = $"{ApiService.WEB_ORIGIN}/learn/course/learn/{courseIid}";
            var headers = new Dictionary<string, string>
            {
                { "Referer", requestUrl },
                { "Origin", ApiService.WEB_ORIGIN }
            };

            try
            {
                // Switch to GET as requested by the user's provided URL format
                var response = await _apiService.GetAsync<JObject>(endpoint, headers);
                return response;
            }
            catch (Exception ex)
            {
                AppLog.Error($"Lỗi lấy trạng thái ngân hàng câu hỏi (GET): {ex.Message}", 0, _authService.CurrentUserIid);
                return null;
            }
        }
    }
}
