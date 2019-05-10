﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Blog.Core.Common.Helper;
using Blog.Core.IServices;
using Blog.Core.Model;
using Blog.Core.Model.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Core.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    [Authorize(PermissionNames.Permission)]
    public class PermissionController : ControllerBase
    {
        readonly IPermissionServices _permissionServices;
        readonly IModuleServices _moduleServices;
        readonly IRoleModulePermissionServices _roleModulePermissionServices;
        readonly IUserRoleServices _userRoleServices;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="permissionServices"></param>
        /// <param name="moduleServices"></param>
        /// <param name="roleModulePermissionServices"></param>
        /// <param name="userRoleServices"></param>
        public PermissionController(IPermissionServices permissionServices, IModuleServices moduleServices, IRoleModulePermissionServices roleModulePermissionServices, IUserRoleServices userRoleServices)
        {
            _permissionServices = permissionServices;
            _moduleServices = moduleServices;
            _roleModulePermissionServices = roleModulePermissionServices;
            _userRoleServices = userRoleServices;

        }

        // GET: api/User
        [HttpGet]
        public async Task<MessageModel<PageModel<Permission>>> Get(int page = 1, string key = "")
        {
            var data = new MessageModel<PageModel<Permission>>();
            int intTotalCount = 50;
            int totalCount = 0;
            int pageCount = 1;

            var permissions = await _permissionServices.Query(a => a.IsDeleted != true);

            if (!string.IsNullOrEmpty(key))
            {
                permissions = permissions.Where(t => (t.Name != null && t.Name.Contains(key))).ToList();
            }


            //筛选后的数据总数
            totalCount = permissions.Count;
            //筛选后的总页数
            pageCount = (Math.Ceiling(totalCount.ObjToDecimal() / intTotalCount.ObjToDecimal())).ObjToInt();

            permissions = permissions.OrderByDescending(d => d.Id).Skip((page - 1) * intTotalCount).Take(intTotalCount).ToList();
            var apis = await _moduleServices.Query(d => d.IsDeleted == false);

            foreach (var item in permissions)
            {
                List<int> pidarr = new List<int>();
                pidarr.Add(item.Pid);
                if (item.Pid > 0)
                {
                    pidarr.Add(0);
                }
                var parent = permissions.FirstOrDefault(d => d.Id == item.Pid);

                while (parent != null)
                {
                    pidarr.Add(parent.Id);
                    parent = permissions.FirstOrDefault(d => d.Id == parent.Pid);
                }


                item.PidArr = pidarr.OrderBy(d => d).Distinct().ToList();
                foreach (var pid in item.PidArr)
                {
                    var per = permissions.FirstOrDefault(d => d.Id == pid);
                    item.PnameArr.Add((per != null ? per.Name : "根节点") + "/");
                    //var par = Permissions.Where(d => d.Pid == item.Id ).ToList();
                    //item.PCodeArr.Add((per != null ? $"/{per.Code}/{item.Code}" : ""));
                    //if (par.Count == 0 && item.Pid == 0)
                    //{
                    //    item.PCodeArr.Add($"/{item.Code}");
                    //}
                }

                item.MName = apis.FirstOrDefault(d => d.Id == item.Mid)?.LinkUrl;
            }

            return new MessageModel<PageModel<Permission>>()
            {
                msg = "获取成功",
                success = totalCount >= 0,
                response = new PageModel<Permission>()
                {
                    page = page,
                    pageCount = pageCount,
                    dataCount = totalCount,
                    data = permissions,
                }
            };

        }

        // GET: api/User/5
        [HttpGet("{id}")]
        public string Get(string id)
        {
            return "value";
        }

        // POST: api/User
        [HttpPost]
        public async Task<MessageModel<string>> Post([FromBody] Permission permission)
        {
            var data = new MessageModel<string>();

            var id = (await _permissionServices.Add(permission));
            data.success = id > 0;
            if (data.success)
            {
                data.response = id.ObjToString();
                data.msg = "添加成功";
            }

            return data;
        }


        [HttpPost]
        public async Task<MessageModel<string>> Assign([FromBody] AssignView assignView)
        {
            var data = new MessageModel<string>();

            try
            {
                if (assignView.rid > 0)
                {

                    data.success = true;

                    var roleModulePermissions = await _roleModulePermissionServices.Query(d => d.RoleId == assignView.rid);

                    var remove = roleModulePermissions.Where(d => !assignView.pids.Contains(d.PermissionId.ObjToInt())).Select(c => (object)c.Id);
                    data.success |= await _roleModulePermissionServices.DeleteByIds(remove.ToArray());

                    foreach (var item in assignView.pids)
                    {
                        var rmpitem = roleModulePermissions.Where(d => d.PermissionId == item);
                        if (!rmpitem.Any())
                        {
                            var moduleid = (await _permissionServices.Query(p => p.Id == item)).FirstOrDefault()?.Mid;
                            RoleModulePermission roleModulePermission = new RoleModulePermission()
                            {
                                IsDeleted = false,
                                RoleId = assignView.rid,
                                ModuleId = moduleid.ObjToInt(),
                                PermissionId = item,
                            };

                            data.success |= (await _roleModulePermissionServices.Add(roleModulePermission)) > 0;

                        }
                    }

                    if (data.success)
                    {
                        data.response = "";
                        data.msg = "保存成功";
                    }

                }
            }
            catch (Exception)
            {
                data.success = false;
            }

            return data;
        }

        [HttpGet]
        public async Task<MessageModel<PermissionTree>> GetPermissionTree(int pid = 0, bool needbtn = false)
        {
            var data = new MessageModel<PermissionTree>();

            var permissions = await _permissionServices.Query(d => d.IsDeleted == false);
            var permissionTrees = (from child in permissions
                                   where child.IsDeleted == false
                                   orderby child.Id
                                   select new PermissionTree
                                   {
                                       value = child.Id,
                                       label = child.Name,
                                       Pid = child.Pid,
                                       isbtn = child.IsButton,
                                       order = child.OrderSort,
                                   }).ToList();
            PermissionTree rootRoot = new PermissionTree();
            rootRoot.value = 0;
            rootRoot.Pid = 0;
            rootRoot.label = "根节点";

            permissionTrees = permissionTrees.OrderBy(d => d.order).ToList();


            RecursionHelper.LoopToAppendChildren(permissionTrees, rootRoot, pid, needbtn);

            data.success = true;
            if (data.success)
            {
                data.response = rootRoot;
                data.msg = "获取成功";
            }

            return data;
        }


        [HttpGet]
        [AllowAnonymous]
        public async Task<MessageModel<NavigationBar>> GetNavigationBar(int uid)
        {
            var data = new MessageModel<NavigationBar>();

            if (uid > 0)
            {
                var roleId = ((await _userRoleServices.Query(d => d.IsDeleted == false && d.UserId == uid)).FirstOrDefault()?.RoleId).ObjToInt();
                if (roleId > 0)
                {
                    var pids = (await _roleModulePermissionServices.Query(d => d.IsDeleted == false && d.RoleId == roleId)).Select(d => d.PermissionId.ObjToInt()).Distinct();

                    if (pids.Any())
                    {
                        var rolePermissionMoudles = (await _permissionServices.Query(d => pids.Contains(d.Id) && d.IsButton == false)).OrderBy(c => c.OrderSort);
                        var permissionTrees = (from child in rolePermissionMoudles
                                               where child.IsDeleted == false
                                               orderby child.Id
                                               select new NavigationBar
                                               {
                                                   id = child.Id,
                                                   name = child.Name,
                                                   pid = child.Pid,
                                                   order = child.OrderSort,
                                                   path = child.Code,
                                                   iconCls = child.Icon,
                                                   IsHide = child.IsHide.ObjToBool(),
                                                   meta = new NavigationBarMeta
                                                   {
                                                       requireAuth = true,
                                                       title = child.Name,
                                                       NoTabPage=child.IsHide.ObjToBool()
                                                   }
                                               }).ToList();


                        NavigationBar rootRoot = new NavigationBar()
                        {
                            id = 0,
                            pid = 0,
                            order = 0,
                            name = "根节点",
                            path = "",
                            iconCls = "",
                            meta = new NavigationBarMeta(),

                        };

                        permissionTrees = permissionTrees.OrderBy(d => d.order).ToList();

                        RecursionHelper.LoopNaviBarAppendChildren(permissionTrees, rootRoot);

                        data.success = true;
                        if (data.success)
                        {
                            data.response = rootRoot;
                            data.msg = "获取成功";
                        }
                    }
                }
            }
            return data;
        }


        [HttpGet]
        [AllowAnonymous]
        public async Task<MessageModel<AssignShow>> GetPermissionIdByRoleId(int rid = 0)
        {
            var data = new MessageModel<AssignShow>();

            var rmps = await _roleModulePermissionServices.Query(d => d.IsDeleted == false && d.RoleId == rid);
            var permissionTrees = (from child in rmps
                                   orderby child.Id
                                   select child.PermissionId.ObjToInt()).ToList();

            var permissions = await _permissionServices.Query(d => d.IsDeleted == false);
            List<string> assignbtns = new List<string>();

            foreach (var item in permissionTrees)
            {
                var pername = permissions.FirstOrDefault(d => d.IsButton && d.Id == item)?.Name;
                if (!string.IsNullOrEmpty(pername))
                {
                    assignbtns.Add(pername + "_" + item);
                }
            }

            data.success = true;
            if (data.success)
            {
                data.response = new AssignShow()
                {
                    permissionids = permissionTrees,
                    assignbtns = assignbtns,
                };
                data.msg = "获取成功";
            }

            return data;
        }


        // PUT: api/User/5
        [HttpPut]
        public async Task<MessageModel<string>> Put([FromBody] Permission permission)
        {
            var data = new MessageModel<string>();
            if (permission != null && permission.Id > 0)
            {
                data.success = await _permissionServices.Update(permission);
                if (data.success)
                {
                    data.msg = "更新成功";
                    data.response = permission?.Id.ObjToString();
                }
            }

            return data;
        }

        // DELETE: api/ApiWithActions/5
        [HttpDelete]
        public async Task<MessageModel<string>> Delete(int id)
        {
            var data = new MessageModel<string>();
            if (id > 0)
            {
                var userDetail = await _permissionServices.QueryById(id);
                userDetail.IsDeleted = true;
                data.success = await _permissionServices.Update(userDetail);
                if (data.success)
                {
                    data.msg = "删除成功";
                    data.response = userDetail?.Id.ObjToString();
                }
            }

            return data;
        }
    }

    public class AssignView
    {
        public List<int> pids { get; set; }
        public int rid { get; set; }
    }
    public class AssignShow
    {
        public List<int> permissionids { get; set; }
        public List<string> assignbtns { get; set; }
    }

}
