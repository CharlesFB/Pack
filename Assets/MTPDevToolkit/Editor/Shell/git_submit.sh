#! /bin/bash

# 获取当前分支名称
function gitbranch {
  br=`git rev-parse --abbrev-ref HEAD`
  echo ${br/* /}
}
branch=`gitbranch`

git pull --rebase=true --autostash origin $branch --verbose

git add .

git commit -m "[MOD] auto-commit by devtoolkit!"

git pull --rebase=true --autostash origin $branch --verbos

git push origin refs/heads/$branch --verbose