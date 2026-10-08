import urllib.request
url = "http://api.weatherapi.com/v1/forecast.json?key=bc0cfac1c3144fc3bf0145134260810&q=tampa&days=1&aqi=no&alerts=yes"
txt = urllib.request.urlopen(url).read().decode()
idx = txt.find('"alerts"')
if idx != -1:
    print(txt[idx:idx+150])
else:
    print("Not found")
