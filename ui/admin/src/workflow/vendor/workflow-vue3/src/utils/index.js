import { NodeType } from './const.js'
function All() {}
All.prototype = {
    timer: "",
    debounce(fn, delay = 500) {
        var _this = this;
        return function(arg) {
            //获取函数的作用域和变量
            let that = this;
            let args = arg;
            clearTimeout(_this.timer) // 清除定时器
            _this.timer = setTimeout(function() {
                fn.call(that, args)
            }, delay)
        }
    },
    setCookie(val) { //cookie设置[{key:value}]、获取key、清除['key1','key2']
        for (var i = 0, len = val.length; i < len; i++) {
            for (var key in val[i]) {
                document.cookie = key + '=' + encodeURIComponent(val[i][key]) + "; path=/";
            }
        }
    },
    getCookie(name) {
        var strCookie = document.cookie;
        var arrCookie = strCookie.split("; ");
        for (var i = 0, len = arrCookie.length; i < len; i++) {
            var arr = arrCookie[i].split("=");
            if (name == arr[0]) {
                return decodeURIComponent(arr[1]);
            }
        }
    },
    clearCookie(name) {
        var myDate = new Date();
        myDate.setTime(-1000); //设置时间
        for (var i = 0, len = name.length; i < len; i++) {
            document.cookie = "" + name[i] + "=''; path=/; expires=" + myDate.toGMTString();
        }
    },
    arrToStr(arr) {
        if (arr) {
            return arr.map(item => { return item.name }).toString()
        }
    },
    toggleClass(arr, elem, key = 'id') {
        return arr.some(item => { return item[key] == elem[key] });
    },
    toChecked(arr, elem, key = 'id') {
        var isIncludes = this.toggleClass(arr, elem, key);
        !isIncludes ? arr.push(elem) : this.removeEle(arr, elem, key);
    },
    removeEle(arr, elem, key = 'id') {
        var includesIndex;
        arr.map((item, index) => {
            if (item[key] == elem[key]) {
                includesIndex = index
            }
        });
        arr.splice(includesIndex, 1);
    },
    setApproverStr(nodeConfig) {
        if (nodeConfig.noHanderAction == 3) {
            if (!nodeConfig.emptyNodeUserList || nodeConfig.emptyNodeUserList.length == 0) return ""
        }
        if (Number(nodeConfig?.type) === NodeType.GroupApprover) {
            const groupList = Array.isArray(nodeConfig.groupList) ? nodeConfig.groupList : []
            if (!groupList.length) return ""
            for (const group of groupList) {
                const groupSetType = String(group?.setType || group?.settype || 'member')
                if (['member', 'role', 'post', 'dept'].includes(groupSetType)) {
                    if (!group || !Array.isArray(group.nodeUserList) || group.nodeUserList.length === 0) return ""
                } else if (groupSetType === 'formUser') {
                    if (!String(group?.formUserFieldKey || '').trim()) return ""
                } else if (groupSetType === 'formDept') {
                    if (!String(group?.formDeptFieldKey || '').trim()) return ""
                } else if (groupSetType === 'syncNode') {
                    if (!String(group?.syncNodeId || '').trim()) return ""
                }
            }
            const passCount = Number(nodeConfig.groupPassCount)
            if (!Number.isInteger(passCount) || passCount < 1 || passCount > groupList.length) return ""
            return `需通过${passCount}组`
        }
        if (Number(nodeConfig?.type) === NodeType.Rob) {
            const robSetType = nodeConfig.robSetType || 'member'
            const labelMap = {
                member: '指定成员',
                deptLevel: '根据部门层级',
                post: '指定岗位',
                tag: '指定标签',
                starterDept: '发起人部门',
                relationOperator: '关联人是-经办人',
                remote: '远程加载',
                starterDeptDirect: '发起人的部门',
                starterDeptCascade: '发起人的连续多级部门',
                starterSelf: '发起人自己',
                formUser: '表单中的人员',
                relationParticipant: '关联人是-参与人',
                role: '指定角色',
                dept: '指定部门',
                formDept: '表单中的部门',
                line: '直属上下级人员',
                syncNode: '同步他节点',
            }
            if (['member', 'role', 'post', 'dept'].includes(robSetType)) {
                if (!nodeConfig.nodeUserList || nodeConfig.nodeUserList.length === 0) return ""
                return this.arrToStr(nodeConfig.nodeUserList)
            }
            if (robSetType === 'starterSelf') return '发起人自己'
            if (robSetType === 'formUser') return nodeConfig.formUserFieldKey ? ('表单字段：' + nodeConfig.formUserFieldKey) : ""
            if (robSetType === 'formDept') return nodeConfig.formDeptFieldKey ? ('表单字段：' + nodeConfig.formDeptFieldKey) : ""
            if (robSetType === 'syncNode') return nodeConfig.syncNodeId ? ('同步节点：' + nodeConfig.syncNodeId) : ""
            return labelMap[robSetType] || ""
        }
        if (nodeConfig.settype == 1) {
            if (nodeConfig.nodeUserList.length == 1) {
                return nodeConfig.nodeUserList[0].name
            } else if (nodeConfig.nodeUserList.length > 1) {
                if (nodeConfig.examineMode == 1) {
                    return this.arrToStr(nodeConfig.nodeUserList)
                } else if (nodeConfig.examineMode == 2) {
                    let rate = nodeConfig.countersignPassRate == null ? 100 : Number(nodeConfig.countersignPassRate)
                    if (Number.isNaN(rate) || rate < 1 || rate > 100) return ""
                    return nodeConfig.nodeUserList.length + "人会签(" + rate + "%)"
                } else if (nodeConfig.examineMode == 3) {
                    return nodeConfig.nodeUserList.length + "人或签"
                } else if (nodeConfig.examineMode == 4) {
                    return nodeConfig.nodeUserList.length + "人并签"
                }
            }
        } else if (nodeConfig.settype == 2) {
            let level = nodeConfig.directorLevel == 1 ? '直接主管' : '第' + nodeConfig.directorLevel + '级主管'
            if (nodeConfig.examineMode == 1) {
                return level
            } else if (nodeConfig.examineMode == 2) {
                return level + "会签"
            } else if (nodeConfig.examineMode == 3) {
                return level + "或签"
            } else if (nodeConfig.examineMode == 4) {
                return level + "并签"
            }
        } else if (nodeConfig.settype == 3) {
            if (!nodeConfig.nodeUserList || nodeConfig.nodeUserList.length == 0) return ""
            return this.arrToStr(nodeConfig.nodeUserList)
        } else if (nodeConfig.settype == 4) {
            if (nodeConfig.selectRange == 1) {
                return "发起人自选"
            } else {
                if (nodeConfig.nodeUserList.length > 0) {
                    if (nodeConfig.selectRange == 2) {
                        return "发起人自选"
                    } else {
                        return '发起人从' + nodeConfig.nodeUserList[0].name + '中自选'
                    }
                } else {
                    return "";
                }
            }
        } else if (nodeConfig.settype == 5) {
            return "发起人自己"
        } else if (nodeConfig.settype == 6) {
            if (!nodeConfig.nodeUserList || nodeConfig.nodeUserList.length == 0) return ""
            return this.arrToStr(nodeConfig.nodeUserList)
        } else if (nodeConfig.settype == 7) {
            return '从直接主管到通讯录中级别最高的第' + nodeConfig.examineEndDirectorLevel + '个层级主管'
        } else if (nodeConfig.settype == 8) {
            if (!nodeConfig.nodeUserList || nodeConfig.nodeUserList.length == 0) return ""
            return this.arrToStr(nodeConfig.nodeUserList)
        } else if (nodeConfig.settype == 9) {
            return nodeConfig.formUserFieldKey ? ('表单字段：' + nodeConfig.formUserFieldKey) : ""
        }
    },
    dealStr(str, obj) {
        let arr = [];
        let list = str.split(",");
        for (var elem in obj) {
            list.map(item => {
                if (item == elem) {
                    arr.push(obj[elem].value)
                }
            })
        }
        return arr.join("或")
    },
    conditionStr(nodeConfig, index) {
        if (Number(nodeConfig?.type) === NodeType.ParallelRoute) return '所有分支都会执行'
        var currentNode = nodeConfig.conditionNodes[index];
        if (currentNode && currentNode.conditionType === 'remote') return 'Full.NET 不支持远程条件'
        var conditionGroups = Array.isArray(currentNode.conditionGroupList) && currentNode.conditionGroupList.length
            ? currentNode.conditionGroupList
            : [{ conditionList: currentNode.conditionList || [], nodeUserList: currentNode.nodeUserList || [] }];
        var hasAnyCondition = conditionGroups.some(group => Array.isArray(group.conditionList) && group.conditionList.length > 0);
        if (!hasAnyCondition) {
            var firstNode = nodeConfig.conditionNodes[0] || {};
            var firstGroups = Array.isArray(firstNode.conditionGroupList) && firstNode.conditionGroupList.length
                ? firstNode.conditionGroupList
                : [{ conditionList: firstNode.conditionList || [], nodeUserList: firstNode.nodeUserList || [] }];
            var firstHasCondition = firstGroups.some(group => Array.isArray(group.conditionList) && group.conditionList.length > 0);
            return (index == nodeConfig.conditionNodes.length - 1) && firstHasCondition ? '其他条件进入此流程' : '请设置条件'
        } else {
            let groupStrList = []
            const isEmptyValue = (val) => !String(val ?? '').trim()
            for (var g = 0; g < conditionGroups.length; g++) {
                let strList = []
                var { conditionList, groupType } = conditionGroups[g];
                var joiner = groupType === 'or' ? ' 或者 ' : ' 并且 ';
                for (var i = 0; i < conditionList.length; i++) {
                    var item = conditionList[i];
                    var { columnId, columnType, showType, showName, optType, zdy1, opt1, zdy2, opt2, fixedDownBoxValue, systemType, nodeUserList } = item;

                    if (columnId == 0) {
                        if (!systemType) return '请设置条件'
                        if (['user', 'dept', 'role', 'post'].includes(systemType)) {
                            if (!optType) return '请设置条件'
                            if (!['dept', 'role', 'post'].includes(systemType) || !['1', '2'].includes(String(optType))) {
                                if (!Array.isArray(nodeUserList) || nodeUserList.length === 0) return '请设置条件'
                            }
                        } else if (systemType === 'isLeader') {
                            if (!optType) return '请设置条件'
                        } else if (['posLevel', 'posTitle'].includes(systemType)) {
                            if (!optType) return '请设置条件'
                            if (String(optType) === '6') {
                                if (isEmptyValue(zdy1) || isEmptyValue(zdy2)) return '请设置条件'
                            } else {
                                if (isEmptyValue(zdy1)) return '请设置条件'
                            }
                        }
                    } else if (columnType == "String" && showType == "3") {
                        if (isEmptyValue(zdy1)) return '请设置条件'
                    } else if (String(optType) === '6') {
                        if (isEmptyValue(zdy1) || isEmptyValue(zdy2)) return '请设置条件'
                    } else {
                        if (isEmptyValue(zdy1)) return '请设置条件'
                    }

                    if (columnId == 0 && !systemType) {
                         if (nodeUserList && nodeUserList.length != 0) {
                            strList.push('发起人属于：' + nodeUserList.map(item => { return item.name }).join("或"))
                        }
                    } else if (['dept', 'user', 'role', 'post', 'isLeader', 'notEmpty', 'empty', 'posLevel', 'posTitle'].includes(systemType)) {
                        if (systemType === 'notEmpty') {
                            strList.push(`${showName}不为空`);
                        } else if (systemType === 'empty') {
                            strList.push(`${showName}为空`);
                        } else if (systemType === 'isLeader') {
                            strList.push(optType == '1' ? '是部门主管' : '不是部门主管');
                        } else if (['posLevel', 'posTitle'].includes(systemType)) {
                            var label = systemType === 'posLevel' ? '职级' : '职称';
                            if (optType != 6 && zdy1) {
                                var optTypeStr = ["", "<", ">", "≤", "=", "≥"][optType]
                                strList.push(`${label} ${optTypeStr} ${zdy1}`)
                            } else if (optType == 6 && zdy1 && zdy2) {
                                strList.push(`${zdy1} ${opt1} ${label} ${opt2} ${zdy2}`)
                            }
                        } else {
                            const labelMap = ['dept'].includes(systemType)
                                ? { '1': '为空', '2': '不为空', '3': '同级属于', '4': '同级不属于', '5': '属于同级及子级', '6': '不属于同级及子级', '7': '包含同级及子级', '8': '不包含同级及子级' }
                                : (['role', 'post'].includes(systemType)
                                    ? { '1': '为空', '2': '不为空', '3': '属于', '4': '不属于', '5': '包含', '6': '不包含', '7': '重合' }
                                    : { '1': '属于', '2': '不属于' });
                            const optLabel = labelMap[optType] || '';
                            if (['1', '2'].includes(optType) && ['dept', 'role', 'post'].includes(systemType)) {
                                strList.push(`${showName} ${optLabel}`)
                            } else {
                                if (nodeUserList && nodeUserList.length != 0) {
                                    strList.push(`${showName} ${optLabel}：` + nodeUserList.map(item => { return item.name }).join("或"))
                                }
                            }
                        }
                    } else if (columnType == "String" && showType == "3") {
                        if (zdy1) {
                            strList.push(showName + '属于：' + this.dealStr(zdy1, JSON.parse(fixedDownBoxValue)))
                        }
                    } else if (columnType == "Double") {
                        if (optType != 6 && zdy1) {
                            var optTypeStr = ["", "<", ">", "≤", "=", "≥"][optType]
                            strList.push(`${showName} ${optTypeStr} ${zdy1}`)
                        } else if (optType == 6 && zdy1 && zdy2) {
                            strList.push(`${zdy1} ${opt1} ${showName} ${opt2} ${zdy2}`)
                        }
                    } else {
                         if (optType != 6 && zdy1) {
                            var optTypeStr = ["", "<", ">", "≤", "=", "≥"][optType]
                            strList.push(`${showName} ${optTypeStr} ${zdy1}`)
                        }
                    }
                }
                if (strList.length > 0) {
                    groupStrList.push(strList.join(joiner))
                }
            }
            const groupMode = currentNode.conditionGroupMode === 'custom' ? 'custom' : 'fixed'
            if (groupMode === 'custom') {
                const expression = String(currentNode.conditionGroupExpression || '').trim()
                if (!expression) return '请设置条件'
                const rendered = expression.replace(/\b\d+\b/g, (match) => {
                    const idx = Number(match) - 1
                    if (!Number.isFinite(idx)) return match
                    if (idx < 0 || idx >= groupStrList.length) return match
                    const text = groupStrList[idx]
                    return text ? `(${text})` : match
                })
                return rendered
            }
            const groupJoiner = currentNode.conditionGroupType === 'or' ? ' 或者 ' : ' 并且 '
            return groupStrList.length ? groupStrList.join(groupJoiner) : '请设置条件'
        }
    },
    copyerStr(nodeConfig) {
        if (nodeConfig.nodeUserList.length != 0) {
            return this.arrToStr(nodeConfig.nodeUserList)
        } else {
            if (nodeConfig.ccSelfSelectFlag == 1) {
                return "发起人自选"
            }
        }
    },
    toggleStrClass(item, key) {
        let a = item.zdy1 ? item.zdy1.split(",") : []
        return a.some(item => { return item == key });
    },
}

export default new All();
