library(rzmq)
library(msgpackR)
library(processx)

# Exercise the Interactive protocol

apsimDir <- "/home/uqpdevo1/src/ApsimX-ZMQ"
#apsimDir <- "/usr/local/lib/apsim/"

# Set up a listening server on a random port
testProto2 <- function() {
  apsim <- list()
  
  # The simulation will connect back to this port:
  apsim$apsimSocket <- open_zmq2(port = 0)
  apsim$randomPort <- unlist(strsplit(get.last.endpoint(apsim$apsimSocket),":"))[3]
  cat("Listening on", get.last.endpoint(apsim$apsimSocket), "\n")
  
  apsim$process <- process$new("/usr/bin/dotnet", args=c(
    paste0(apsimDir, "/bin/Debug/net8.0/ApsimZMQServer.dll"), 
    "-p", apsim$randomPort, 
    "-P", "interactive",
    "-f", paste0(apsimDir, "/Tests/Simulation/ZMQ-Sync/ZMQ-sync.apsimx")),
    stdout="", stderr="")

  cat("Started Apsim process id ", apsim$process$get_pid(), "\n")
  return(apsim)
}

# Set up a listening (response) socket that the simulation will connect to
open_zmq2 <- function(port) {
  zcontext <<- init.context()  # This needs to be kept around (global environment)..
  out.socket <- init.socket(zcontext, "ZMQ_REP")
  bind.socket(out.socket, paste0("tcp://0.0.0.0:", port))
  return(out.socket)
}

close_zmq2 <- function(apsim) {
  disconnect.socket(apsim$apsimSocket, get.last.endpoint(apsim$apsimSocket))
  # kill off apsim server
  apsim$process$kill()
}

# Send a command, eg resume/set/get
sendCommand <- function(socket, command, args = NULL) {
  send.raw.string(socket, command, send.more = !is.null(args))
  if (!is.null(args)) {
     for (i in 1:length(args)) {
        send.socket(socket, msgpackR::pack(args[[i]]), serialize = F, send.more = (i != length(args)))
     }
  }
}
# The response loop. When the simulation connects we tell it what to do.
# connect -> ok
# paused -> resume/get/set/do
# finished -> ok
poll_zmq2 <- function(socket) {
  while (TRUE) {
    msg <- receive.string(socket)
    # cat(msg, "\n")
    if (msg == "connect") {
      sendCommand(socket, "ok")
    } else if (msg == "paused") {
      # Each send is followed by a receive. "set" responds with a string "ok",
      # "get" responds with a packed byte array that we pass to the deserialiser
      #sendCommand(socket, "get", "[Clock].Today.Day")
      #reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      #stopifnot(class(reply) == "numeric")

      sendCommand(socket, "get", "[Manager].Script.cumsumfert")
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      cat("cumsumfert=", reply, "\n")

      sendCommand(socket, "get", "[Manager].Script.dap")
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      cat("dap=", reply, "\n")

      sendCommand(socket, "get", "[Maize].Phenology.ThermalTime")
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      cat("dtt=", reply, "\n")

      sendCommand(socket, "get", "[Manager].Script.vstage")
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      cat("vstage=", reply, "\n")

      sendCommand(socket, "get", "[Maize].SowingData.Population")
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      cat("pltpop=", reply, "\n")

      sendCommand(socket, "get", "[Weather].Rain")
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      cat("rain=", reply, "\n")

      sendCommand(socket, "get", "[Weather].MaxT")
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      cat("tmax=", reply, "\n")

      sendCommand(socket, "get", "[Weather].MinT")
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      cat("tmin=", reply, "\n")

      sendCommand(socket, "get", "[Weather].Radn")
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      cat("srad=", reply, "\n")

      sendCommand(socket, "get", "[Maize].Leaf.Fn") 
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      #reply <- 1 - reply         # might be (1-stress)??
      cat("nstres=", reply, "\n")

      # pcngrn <- no ideas on this

      sendCommand(socket, "get", "[Maize].Leaf.Fw") 
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      #reply <- 1 - reply         # might be (1-stress)??
      cat("swfac=", reply, "\n")

      sendCommand(socket, "get", "[Soil].SoilWater.LeachNO3") 
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      cat("tleach=", reply, "\n")

      sendCommand(socket, "get", "[Maize].Grain.Wt") 
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      reply <- reply * 10        # g/m2 -> kg/ha
      cat("grnwt=", reply, "\n")

      sendCommand(socket, "get", "[Manager].Script.dnit") 
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      cat("tnox=", reply, "\n")

      sendCommand(socket, "get", "[Maize].Root.NUptake") 
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      reply <- reply * -1        # uptake is reported as a change
      cat("trnu=", reply, "\n")
    
      sendCommand(socket, "get", "[Maize].Leaf.LAI") 
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      cat("xlai=", reply, "\n")

      sendCommand(socket, "get", "[Maize].AboveGround.Wt") 
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      reply <- reply * 10        # g/m2 -> kg/ha
      cat("topwt=", reply, "\n")
    
      sendCommand(socket, "get", "[Soil].SoilWater.Es") 
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      cat("es=", reply, "\n")
     
      sendCommand(socket, "get", "[Soil].SoilWater.Runoff") 
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      cat("runoff=", reply, "\n")

      #sendCommand(socket, "get", "[Soil]....") 
      #reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      #cat("wtdep=", NA, "\n") # apsim doesnt do watertables

      sendCommand(socket, "get", "[Maize].Root.Depth") 
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      cat("rtdep=", reply, "\n")

      #sendCommand(socket, "get", "[Soil]....") 
      #reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      #cat("totaml=", NA, "\n") # apsim doesnt do NH4 volatisation - see https://yunrulai.com/talk/wcss22/ for ideas

      sendCommand(socket, "get", "[Soil].SoilWater.SW") 
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      cat("sw=", reply, "\n") # Probably better looking at ESW/PAW 

      sendCommand(socket, "get", "[Clock].Today.day")
      reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
      cat("day=", reply, "\n")
      if (reply == 2) {
         sendCommand(socket, "do", list("addFertiliser", "amount", 40))
         reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
         cat("do reply=", reply, "\n")
      }

      if (reply == 3) {
         sendCommand(socket, "do", list("sowCrop", "cropName", "Maize", "cultivarName", "sc501"))
         reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
         cat("do reply=", reply, "\n")
      }

      if (reply == 4) {
         sendCommand(socket, "do", list("tillage", "type", "disc"))
         reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
         cat("do reply=", reply, "\n")
      }

      if (reply == 5) {
         sendCommand(socket, "do", list("applyIrrigation", "amount", 42))
         reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
         cat("do reply=", reply, "\n")
      }

      if (reply == 6) {
         sendCommand(socket, "do", list("terminate"))
         reply <- msgpackR::unpack(receive.socket(socket, unserialize = F))
         cat("do reply=", reply, "\n")
      }


      # resume the simulation and come back tomorrow
      sendCommand(socket, "resume")
    } else if (msg == "finished") {
      sendCommand(socket, "ok")
      break
    } 
  }
}

apsim <- testProto2()
poll_zmq2(apsim$apsimSocket)
close_zmq2(apsim)
