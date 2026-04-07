using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LotusLMSClient.Models
{
    public class SubjectDetails
    {


        public bool success { get; set; }
        public Result result { get; set; }
        public string ts { get; set; }
        public int server_ts { get; set; }
        public string request_id { get; set; }
        public string server_ip { get; set; }
        public int translate { get; set; }


        public class Result
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string name { get; set; }
            public string ntype { get; set; }
            public string code { get; set; }
            public string type { get; set; }
            public string avatar { get; set; }
            public U u { get; set; }
            public string slug { get; set; }
            public int count { get; set; }
            public string sub_type { get; set; }
            public int[] organizations { get; set; }
            public int[] academic_categories { get; set; }
            public string status { get; set; }
            public string overview_layout { get; set; }
            public int online_only { get; set; }
            public string sequential_learning_type { get; set; }
            public Child[] children { get; set; }
            public Metadata7[] metadata { get; set; }
            public int is_clone { get; set; }
            public int clone_from { get; set; }
            public string weight_scheme { get; set; }
            public int weighed_score { get; set; }
            public string original_language { get; set; }
            public string aggregated_duration { get; set; }
            public int min_learning_time { get; set; }
            public int children_not_loaded { get; set; }
            public int rubric_iid { get; set; }
            public int max_score { get; set; }
            public int auto_next_item { get; set; }
            public string[] learning_types_v2 { get; set; }
            public int ts { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int total_children_count { get; set; }
            public int[] tracking_line { get; set; }
            public Staff[] staff { get; set; }
            public int depth { get; set; }
        }

        public class U
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string name { get; set; }
            public string code { get; set; }
            public string mail { get; set; }
        }

        public class Child
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public int weighted { get; set; }
            public string name { get; set; }
            public string code { get; set; }
            public U1 u { get; set; }
            public int count { get; set; }
            public int[] organizations { get; set; }
            public string status { get; set; }
            public Child1[] children { get; set; }
            public Metadata3[] metadata { get; set; }
            public int is_clone { get; set; }
            public int clone_from { get; set; }
            public int weighed_score { get; set; }
            public int min_learning_time { get; set; }
            public string sco_page_layout { get; set; }
            public int children_not_loaded { get; set; }
            public int ts { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int total_children_count { get; set; }
            public string tpl_type { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public string duration { get; set; }
            public int actual_learning_time { get; set; }
            public int[] sequential { get; set; }
            public int freeze { get; set; }
            public string aggregated_duration { get; set; }
        }

        public class U1
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string name { get; set; }
            public string code { get; set; }
            public string mail { get; set; }
        }

        public class Child1
        {
            public string type { get; set; }
            public int iid { get; set; }
            public string id { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public int weighted { get; set; }
            public Block_Settings block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public string duration { get; set; }
            public int actual_learning_time { get; set; }
            public Time_Tracking[] time_tracking { get; set; }
            public object[] progress { get; set; }
            public string tpl_type { get; set; }
            public string code { get; set; }
            public int ts { get; set; }
            public int[] sequential { get; set; }
            public U2 u { get; set; }
            public int count { get; set; }
            public int[] organizations { get; set; }
            public string status { get; set; }
            public Child2[] children { get; set; }
            public Metadata1[] metadata { get; set; }
            public int is_clone { get; set; }
            public int weighed_score { get; set; }
            public string spl_display_type { get; set; }
            public int is_sequential_spl { get; set; }
            public string sco_page_layout { get; set; }
            public int children_not_loaded { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int total_children_count { get; set; }
            public int freeze { get; set; }
            public string aggregated_duration { get; set; }
            public Attachment2[] attachments { get; set; }
            public int auto_play { get; set; }
        }

        public class Block_Settings
        {
            public string[] heading_style { get; set; }
        }

        public class U2
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string name { get; set; }
            public string code { get; set; }
            public string mail { get; set; }
        }

        public class Time_Tracking
        {
            public int course_iid { get; set; }
            public int user_iid { get; set; }
            public string item_id { get; set; }
            public int hour_ts { get; set; }
            public string item_name { get; set; }
            public string item_ntype { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int ts { get; set; }
            public int updated_ts { get; set; }
            public int time_spent { get; set; }
            public int date_ts { get; set; }
            public string id { get; set; }
        }

        public class Child2
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings1 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public string duration { get; set; }
            public int actual_learning_time { get; set; }
            public Time_Tracking1[] time_tracking { get; set; }
            public object[] progress { get; set; }
            public Attachment[] attachments { get; set; }
            public int auto_play { get; set; }
            public string code { get; set; }
            public U3 u { get; set; }
            public int count { get; set; }
            public int[] organizations { get; set; }
            public string status { get; set; }
            public Child3[] children { get; set; }
            public Metadata[] metadata { get; set; }
            public int is_clone { get; set; }
            public int weighed_score { get; set; }
            public string spl_display_type { get; set; }
            public int is_sequential_spl { get; set; }
            public string sco_page_layout { get; set; }
            public int children_not_loaded { get; set; }
            public int ts { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int total_children_count { get; set; }
            public string tpl_type { get; set; }
        }

        public class Block_Settings1
        {
            public string[] heading_style { get; set; }
        }

        public class U3
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string name { get; set; }
            public string code { get; set; }
            public string mail { get; set; }
        }

        public class Time_Tracking1
        {
            public int course_iid { get; set; }
            public int user_iid { get; set; }
            public string item_id { get; set; }
            public int hour_ts { get; set; }
            public string item_name { get; set; }
            public string item_ntype { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int ts { get; set; }
            public int updated_ts { get; set; }
            public int time_spent { get; set; }
            public int date_ts { get; set; }
            public string id { get; set; }
        }

        public class Attachment
        {
            public string name { get; set; }
            public string ext { get; set; }
            public string id { get; set; }
            public string link { get; set; }
            public string dt { get; set; }
            public string dti { get; set; }
            public string et { get; set; }
            public string st { get; set; }
            public long size { get; set; }
            public string mp4_url { get; set; }
            public bool is_drm { get; set; }
        }

        public class Child3
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings2 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public object duration { get; set; }
            public int actual_learning_time { get; set; }
            public Time_Tracking2[] time_tracking { get; set; }
            public object[] progress { get; set; }
        }

        public class Block_Settings2
        {
            public string[] heading_style { get; set; }
        }

        public class Time_Tracking2
        {
            public int course_iid { get; set; }
            public int user_iid { get; set; }
            public string item_id { get; set; }
            public int hour_ts { get; set; }
            public string item_name { get; set; }
            public string item_ntype { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int ts { get; set; }
            public int updated_ts { get; set; }
            public int time_spent { get; set; }
            public int date_ts { get; set; }
            public string id { get; set; }
        }

        public class Metadata
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings3 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public object duration { get; set; }
        }

        public class Block_Settings3
        {
            public string[] heading_style { get; set; }
        }

        public class Metadata1
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings4 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public string duration { get; set; }
            public Attachment1[] attachments { get; set; }
            public int auto_play { get; set; }
            public string code { get; set; }
            public U4 u { get; set; }
            public int count { get; set; }
            public int[] organizations { get; set; }
            public string status { get; set; }
            public Child4[] children { get; set; }
            public Metadata2[] metadata { get; set; }
            public int is_clone { get; set; }
            public int weighed_score { get; set; }
            public string spl_display_type { get; set; }
            public int is_sequential_spl { get; set; }
            public string sco_page_layout { get; set; }
            public int children_not_loaded { get; set; }
            public int ts { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int total_children_count { get; set; }
            public string tpl_type { get; set; }
        }

        public class Block_Settings4
        {
            public string[] heading_style { get; set; }
        }

        public class U4
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string name { get; set; }
            public string code { get; set; }
            public string mail { get; set; }
        }

        public class Attachment1
        {
            public string name { get; set; }
            public string ext { get; set; }
            public string id { get; set; }
            public string link { get; set; }
            public string dt { get; set; }
            public string dti { get; set; }
            public string et { get; set; }
            public string st { get; set; }
            public long size { get; set; }
            public string mp4_url { get; set; }
            public bool is_drm { get; set; }
        }

        public class Child4
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings5 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public object duration { get; set; }
        }

        public class Block_Settings5
        {
            public string[] heading_style { get; set; }
        }

        public class Metadata2
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings6 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public object duration { get; set; }
        }

        public class Block_Settings6
        {
            public string[] heading_style { get; set; }
        }

        public class Attachment2
        {
            public string name { get; set; }
            public string ext { get; set; }
            public string id { get; set; }
            public string link { get; set; }
            public string dt { get; set; }
            public string dti { get; set; }
            public string et { get; set; }
            public string st { get; set; }
            public int size { get; set; }
            public string mp4_url { get; set; }
            public bool is_drm { get; set; }
        }

        public class Metadata3
        {
            public string type { get; set; }
            public int iid { get; set; }
            public string id { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public int weighted { get; set; }
            public Block_Settings7 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public string duration { get; set; }
            public string tpl_type { get; set; }
            public string code { get; set; }
            public int ts { get; set; }
            public int[] sequential { get; set; }
            public U5 u { get; set; }
            public int count { get; set; }
            public int[] organizations { get; set; }
            public string status { get; set; }
            public Child5[] children { get; set; }
            public Metadata5[] metadata { get; set; }
            public int is_clone { get; set; }
            public int weighed_score { get; set; }
            public string spl_display_type { get; set; }
            public int is_sequential_spl { get; set; }
            public string sco_page_layout { get; set; }
            public int children_not_loaded { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int total_children_count { get; set; }
            public int freeze { get; set; }
            public string aggregated_duration { get; set; }
            public Attachment5[] attachments { get; set; }
            public int auto_play { get; set; }
        }

        public class Block_Settings7
        {
            public string[] heading_style { get; set; }
        }

        public class U5
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string name { get; set; }
            public string code { get; set; }
            public string mail { get; set; }
        }

        public class Child5
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings8 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public string duration { get; set; }
            public Attachment3[] attachments { get; set; }
            public int auto_play { get; set; }
            public string code { get; set; }
            public U6 u { get; set; }
            public int count { get; set; }
            public int[] organizations { get; set; }
            public string status { get; set; }
            public Child6[] children { get; set; }
            public Metadata4[] metadata { get; set; }
            public int is_clone { get; set; }
            public int weighed_score { get; set; }
            public string spl_display_type { get; set; }
            public int is_sequential_spl { get; set; }
            public string sco_page_layout { get; set; }
            public int children_not_loaded { get; set; }
            public int ts { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int total_children_count { get; set; }
            public string tpl_type { get; set; }
        }

        public class Block_Settings8
        {
            public string[] heading_style { get; set; }
        }

        public class U6
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string name { get; set; }
            public string code { get; set; }
            public string mail { get; set; }
        }

        public class Attachment3
        {
            public string name { get; set; }
            public string ext { get; set; }
            public string id { get; set; }
            public string link { get; set; }
            public string dt { get; set; }
            public string dti { get; set; }
            public string et { get; set; }
            public string st { get; set; }
            public long size { get; set; }
            public string mp4_url { get; set; }
            public bool is_drm { get; set; }
        }

        public class Child6
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings9 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public object duration { get; set; }
        }

        public class Block_Settings9
        {
            public string[] heading_style { get; set; }
        }

        public class Metadata4
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings10 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public object duration { get; set; }
        }

        public class Block_Settings10
        {
            public string[] heading_style { get; set; }
        }

        public class Metadata5
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings11 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public string duration { get; set; }
            public Attachment4[] attachments { get; set; }
            public int auto_play { get; set; }
            public string code { get; set; }
            public U7 u { get; set; }
            public int count { get; set; }
            public int[] organizations { get; set; }
            public string status { get; set; }
            public Child7[] children { get; set; }
            public Metadata6[] metadata { get; set; }
            public int is_clone { get; set; }
            public int weighed_score { get; set; }
            public string spl_display_type { get; set; }
            public int is_sequential_spl { get; set; }
            public string sco_page_layout { get; set; }
            public int children_not_loaded { get; set; }
            public int ts { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int total_children_count { get; set; }
            public string tpl_type { get; set; }
        }

        public class Block_Settings11
        {
            public string[] heading_style { get; set; }
        }

        public class U7
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string name { get; set; }
            public string code { get; set; }
            public string mail { get; set; }
        }

        public class Attachment4
        {
            public string name { get; set; }
            public string ext { get; set; }
            public string id { get; set; }
            public string link { get; set; }
            public string dt { get; set; }
            public string dti { get; set; }
            public string et { get; set; }
            public string st { get; set; }
            public long size { get; set; }
            public string mp4_url { get; set; }
            public bool is_drm { get; set; }
        }

        public class Child7
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings12 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public object duration { get; set; }
        }

        public class Block_Settings12
        {
            public string[] heading_style { get; set; }
        }

        public class Metadata6
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings13 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public object duration { get; set; }
        }

        public class Block_Settings13
        {
            public string[] heading_style { get; set; }
        }

        public class Attachment5
        {
            public string name { get; set; }
            public string ext { get; set; }
            public string id { get; set; }
            public string link { get; set; }
            public string dt { get; set; }
            public string dti { get; set; }
            public string et { get; set; }
            public string st { get; set; }
            public int size { get; set; }
            public string mp4_url { get; set; }
            public bool is_drm { get; set; }
        }

        public class Metadata7
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public int weighted { get; set; }
            public string name { get; set; }
            public string code { get; set; }
            public U8 u { get; set; }
            public int count { get; set; }
            public int[] organizations { get; set; }
            public string status { get; set; }
            public Child8[] children { get; set; }
            public Metadata11[] metadata { get; set; }
            public int is_clone { get; set; }
            public int clone_from { get; set; }
            public int weighed_score { get; set; }
            public int min_learning_time { get; set; }
            public string sco_page_layout { get; set; }
            public int children_not_loaded { get; set; }
            public int ts { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int total_children_count { get; set; }
            public string tpl_type { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public string duration { get; set; }
            public int[] sequential { get; set; }
            public int freeze { get; set; }
            public string aggregated_duration { get; set; }
            public int exam_sco_iid { get; set; }
            public int syllabus_iid { get; set; }
            public string[] modes { get; set; }
            public string cp_calculation_method { get; set; }
            public Practice_Tag_Options practice_tag_options { get; set; }
            public string target_user { get; set; }
            public string practice_version { get; set; }
            public int updated_ts { get; set; }
            public string default_avatar { get; set; }
            public Options options { get; set; }
            public int oi { get; set; }
            public string qdm { get; set; }
            public string help_mode { get; set; }
            public Counter counter { get; set; }
            public string passing_scheme { get; set; }
            public string passing_scheme_data { get; set; }
            public string exercise_type { get; set; }
            public int show_question_answers_after_student_finished_exercise { get; set; }
            public int show_question_answers_after_ended_exercise { get; set; }
            public string slug { get; set; }
            public int dirty { get; set; }
            public int not_allow_redo_when_passed { get; set; }
            public int pass_score { get; set; }
            public string show_result_mode { get; set; }
            public Using_Camera_Check_User_Active using_camera_check_user_active { get; set; }
            public int has_oe_questions { get; set; }
        }

        public class U8
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string name { get; set; }
            public string code { get; set; }
            public string mail { get; set; }
        }

        public class Practice_Tag_Options
        {
            public int questions_per_each_practice_option { get; set; }
            public int show_result_screen { get; set; }
            public string display_list_of_questions_mode { get; set; }
            public int display_questions_overview_in_dashboard { get; set; }
        }

        public class Options
        {
            public int instant_key { get; set; }
            public int can_review { get; set; }
        }

        public class Counter
        {
            public int questions { get; set; }
        }

        public class Using_Camera_Check_User_Active
        {
            public string enable_camera_screenshot { get; set; }
        }

        public class Child8
        {
            public string type { get; set; }
            public int iid { get; set; }
            public string id { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public int weighted { get; set; }
            public Block_Settings14 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public string duration { get; set; }
            public string tpl_type { get; set; }
            public string code { get; set; }
            public int ts { get; set; }
            public int[] sequential { get; set; }
            public U9 u { get; set; }
            public int count { get; set; }
            public int[] organizations { get; set; }
            public string status { get; set; }
            public Child9[] children { get; set; }
            public Metadata9[] metadata { get; set; }
            public int is_clone { get; set; }
            public int weighed_score { get; set; }
            public string spl_display_type { get; set; }
            public int is_sequential_spl { get; set; }
            public string sco_page_layout { get; set; }
            public int children_not_loaded { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int total_children_count { get; set; }
            public int freeze { get; set; }
            public string aggregated_duration { get; set; }
            public Attachment8[] attachments { get; set; }
            public int auto_play { get; set; }
        }

        public class Block_Settings14
        {
            public string[] heading_style { get; set; }
        }

        public class U9
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string name { get; set; }
            public string code { get; set; }
            public string mail { get; set; }
        }

        public class Child9
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings15 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public string duration { get; set; }
            public Attachment6[] attachments { get; set; }
            public int auto_play { get; set; }
            public string code { get; set; }
            public U10 u { get; set; }
            public int count { get; set; }
            public int[] organizations { get; set; }
            public string status { get; set; }
            public Child10[] children { get; set; }
            public Metadata8[] metadata { get; set; }
            public int is_clone { get; set; }
            public int weighed_score { get; set; }
            public string spl_display_type { get; set; }
            public int is_sequential_spl { get; set; }
            public string sco_page_layout { get; set; }
            public int children_not_loaded { get; set; }
            public int ts { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int total_children_count { get; set; }
            public string tpl_type { get; set; }
        }

        public class Block_Settings15
        {
            public string[] heading_style { get; set; }
        }

        public class U10
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string name { get; set; }
            public string code { get; set; }
            public string mail { get; set; }
        }

        public class Attachment6
        {
            public string name { get; set; }
            public string ext { get; set; }
            public string id { get; set; }
            public string link { get; set; }
            public string dt { get; set; }
            public string dti { get; set; }
            public string et { get; set; }
            public string st { get; set; }
            public long size { get; set; }
            public string mp4_url { get; set; }
            public bool is_drm { get; set; }
        }

        public class Child10
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings16 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public object duration { get; set; }
        }

        public class Block_Settings16
        {
            public string[] heading_style { get; set; }
        }

        public class Metadata8
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings17 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public object duration { get; set; }
        }

        public class Block_Settings17
        {
            public string[] heading_style { get; set; }
        }

        public class Metadata9
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings18 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public string duration { get; set; }
            public Attachment7[] attachments { get; set; }
            public int auto_play { get; set; }
            public string code { get; set; }
            public U11 u { get; set; }
            public int count { get; set; }
            public int[] organizations { get; set; }
            public string status { get; set; }
            public Child11[] children { get; set; }
            public Metadata10[] metadata { get; set; }
            public int is_clone { get; set; }
            public int weighed_score { get; set; }
            public string spl_display_type { get; set; }
            public int is_sequential_spl { get; set; }
            public string sco_page_layout { get; set; }
            public int children_not_loaded { get; set; }
            public int ts { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int total_children_count { get; set; }
            public string tpl_type { get; set; }
        }

        public class Block_Settings18
        {
            public string[] heading_style { get; set; }
        }

        public class U11
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string name { get; set; }
            public string code { get; set; }
            public string mail { get; set; }
        }

        public class Attachment7
        {
            public string name { get; set; }
            public string ext { get; set; }
            public string id { get; set; }
            public string link { get; set; }
            public string dt { get; set; }
            public string dti { get; set; }
            public string et { get; set; }
            public string st { get; set; }
            public long size { get; set; }
            public string mp4_url { get; set; }
            public bool is_drm { get; set; }
        }

        public class Child11
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings19 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public object duration { get; set; }
        }

        public class Block_Settings19
        {
            public string[] heading_style { get; set; }
        }

        public class Metadata10
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings20 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public object duration { get; set; }
        }

        public class Block_Settings20
        {
            public string[] heading_style { get; set; }
        }

        public class Attachment8
        {
            public string name { get; set; }
            public string ext { get; set; }
            public string id { get; set; }
            public string link { get; set; }
            public string dt { get; set; }
            public string dti { get; set; }
            public string et { get; set; }
            public string st { get; set; }
            public int size { get; set; }
            public string mp4_url { get; set; }
            public bool is_drm { get; set; }
        }

        public class Metadata11
        {
            public string type { get; set; }
            public int iid { get; set; }
            public string id { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public int weighted { get; set; }
            public Block_Settings21 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public string duration { get; set; }
            public string tpl_type { get; set; }
            public string code { get; set; }
            public int ts { get; set; }
            public int[] sequential { get; set; }
            public U12 u { get; set; }
            public int count { get; set; }
            public int[] organizations { get; set; }
            public string status { get; set; }
            public Child12[] children { get; set; }
            public Metadata13[] metadata { get; set; }
            public int is_clone { get; set; }
            public int weighed_score { get; set; }
            public string spl_display_type { get; set; }
            public int is_sequential_spl { get; set; }
            public string sco_page_layout { get; set; }
            public int children_not_loaded { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int total_children_count { get; set; }
            public int freeze { get; set; }
            public string aggregated_duration { get; set; }
            public Attachment11[] attachments { get; set; }
            public int auto_play { get; set; }
        }

        public class Block_Settings21
        {
            public string[] heading_style { get; set; }
        }

        public class U12
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string name { get; set; }
            public string code { get; set; }
            public string mail { get; set; }
        }

        public class Child12
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings22 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public string duration { get; set; }
            public Attachment9[] attachments { get; set; }
            public int auto_play { get; set; }
            public string code { get; set; }
            public U13 u { get; set; }
            public int count { get; set; }
            public int[] organizations { get; set; }
            public string status { get; set; }
            public Child13[] children { get; set; }
            public Metadata12[] metadata { get; set; }
            public int is_clone { get; set; }
            public int weighed_score { get; set; }
            public string spl_display_type { get; set; }
            public int is_sequential_spl { get; set; }
            public string sco_page_layout { get; set; }
            public int children_not_loaded { get; set; }
            public int ts { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int total_children_count { get; set; }
            public string tpl_type { get; set; }
        }

        public class Block_Settings22
        {
            public string[] heading_style { get; set; }
        }

        public class U13
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string name { get; set; }
            public string code { get; set; }
            public string mail { get; set; }
        }

        public class Attachment9
        {
            public string name { get; set; }
            public string ext { get; set; }
            public string id { get; set; }
            public string link { get; set; }
            public string dt { get; set; }
            public string dti { get; set; }
            public string et { get; set; }
            public string st { get; set; }
            public long size { get; set; }
            public string mp4_url { get; set; }
            public bool is_drm { get; set; }
        }

        public class Child13
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings23 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public object duration { get; set; }
        }

        public class Block_Settings23
        {
            public string[] heading_style { get; set; }
        }

        public class Metadata12
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings24 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public object duration { get; set; }
        }

        public class Block_Settings24
        {
            public string[] heading_style { get; set; }
        }

        public class Metadata13
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings25 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public string duration { get; set; }
            public Attachment10[] attachments { get; set; }
            public int auto_play { get; set; }
            public string code { get; set; }
            public U14 u { get; set; }
            public int count { get; set; }
            public int[] organizations { get; set; }
            public string status { get; set; }
            public Child14[] children { get; set; }
            public Metadata14[] metadata { get; set; }
            public int is_clone { get; set; }
            public int weighed_score { get; set; }
            public string spl_display_type { get; set; }
            public int is_sequential_spl { get; set; }
            public string sco_page_layout { get; set; }
            public int children_not_loaded { get; set; }
            public int ts { get; set; }
            public string dmn { get; set; }
            public string school { get; set; }
            public int total_children_count { get; set; }
            public string tpl_type { get; set; }
        }

        public class Block_Settings25
        {
            public string[] heading_style { get; set; }
        }

        public class U14
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string name { get; set; }
            public string code { get; set; }
            public string mail { get; set; }
        }

        public class Attachment10
        {
            public string name { get; set; }
            public string ext { get; set; }
            public string id { get; set; }
            public string link { get; set; }
            public string dt { get; set; }
            public string dti { get; set; }
            public string et { get; set; }
            public string st { get; set; }
            public long size { get; set; }
            public string mp4_url { get; set; }
            public bool is_drm { get; set; }
        }

        public class Child14
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings26 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public object duration { get; set; }
        }

        public class Block_Settings26
        {
            public string[] heading_style { get; set; }
        }

        public class Metadata14
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string ntype { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int weighted { get; set; }
            public int[] sequential { get; set; }
            public Block_Settings27 block_settings { get; set; }
            public string slug { get; set; }
            public int clone_from { get; set; }
            public int text_to_speech_enabled { get; set; }
            public string content { get; set; }
            public int min_learning_time { get; set; }
            public string editor_tool { get; set; }
            public int depth { get; set; }
            public int pid { get; set; }
            public object duration { get; set; }
        }

        public class Block_Settings27
        {
            public string[] heading_style { get; set; }
        }

        public class Attachment11
        {
            public string name { get; set; }
            public string ext { get; set; }
            public string id { get; set; }
            public string link { get; set; }
            public string dt { get; set; }
            public string dti { get; set; }
            public string et { get; set; }
            public string st { get; set; }
            public int size { get; set; }
            public string mp4_url { get; set; }
            public bool is_drm { get; set; }
        }

        public class Staff
        {
            public string id { get; set; }
            public int iid { get; set; }
            public string code { get; set; }
            public string name { get; set; }
            public string mail { get; set; }
            public string[] roles { get; set; }
        }


    }
}
